using System.Text.RegularExpressions ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal.Helpers ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Utils ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Manages stock transactions and parses Telegram message format. User-scoped methods enforce
///   that the caller is a member of the transaction's parent <see cref="BrokerAccount" /> — not a
///   member → 404 (via <see cref="KeyNotFoundException" />).
///   Every persisted transaction adjusts the broker's linked <see cref="SpendAccount" />.<c>Balance</c>
///   via the signed <c>Amount</c> on <see cref="TransactionBase" />, computed at the boundary by
///   <see cref="StockTransaction.ComputeAmount" />: Buy → negative; Sell, Dividend → positive.
///   Updates reverse the old delta and apply the new;
///   soft-deletes reverse. Balance mutations go through EF change tracking with
///   <c>[ConcurrencyCheck]</c> on <c>Balance</c> — concurrent writers race for the row and the loser
///   gets a 409 <see cref="ConflictException" />. Both the balance change and the transaction write land
///   in one <c>SaveChanges</c> call, so they are implicitly atomic.
/// </summary>
public partial class StockTransactionService(
  IRepository<StockTransaction> stockTransactionRepository,
  IRepository<BrokerAccount> brokerRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository,
  ILogger<StockTransactionService> logger ) : IStockTransactionService
{
  /// <inheritdoc />
  public async Task<StockTransaction?> ParseMessageAsync( string text )
  {
    try
    {
      var parts = text.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) ;
      if ( parts.Length < 2 || ! parts[ 0 ].Equals( "stock", StringComparison.OrdinalIgnoreCase ) )
      {
        return null ;
      }

      var typeStr = parts[ 1 ].ToLowerInvariant() ;

      return typeStr switch
      {
        "buy" or "sell" => await ParseBuySellAsync( text, parts, typeStr ),
        "div" => await ParseDividendAsync( text, parts ),
        _ => null
      } ;
    }
    catch ( Exception ex )
    {
      logger.LogWarning( ex, "Failed to parse stock message: {Text}", text ) ;
      return null ;
    }
  }

  /// <inheritdoc />
  public async Task<StockTransaction> CreateAsync( StockTransaction transaction, long userId )
  {
    var spendId = await brokerRepository.Query()
                    .Where( b => b.Id == transaction.BrokerAccountId
                                 && b.Members.Any( m => m.AppUserId == userId ) )
                    .Select( b => ( long? )b.SpendAccountId )
                    .FirstOrDefaultAsync()
                  ?? throw new KeyNotFoundException(
                    $"BrokerAccount {transaction.BrokerAccountId} not found for user {userId}." ) ;

    await TransactionCategoryGuard.EnsureKindMatchesAsync(
      categoryRepository, transaction.CategoryId, TransactionKind.Stock ) ;

    transaction.SpendAccountId = spendId ;

    var spendAccount = await spendRepository.GetByIdOrThrowAsync( spendId ) ;
    spendAccount.Balance += transaction.Amount ;
    await stockTransactionRepository.AddAsync( transaction ) ;
    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync( stockTransactionRepository, spendId ) ;

    return transaction ;
  }

  /// <inheritdoc />
  public async Task<List<StockTransaction>> GetAllForUserAsync( long userId, string? symbol = null )
  {
    var query = stockTransactionRepository.Query()
      .Where( t => t.BrokerAccount.Members.Any( m => m.AppUserId == userId ) ) ;

    if ( ! string.IsNullOrWhiteSpace( symbol ) )
    {
      var normalizedSymbol = StockTransaction.NormalizeSymbol( symbol ) ;
      query = query.Where( t => t.Symbol == normalizedSymbol ) ;
    }

    return await query.AsNoTracking()
      .Include( t => t.BrokerAccount )
      .Include( t => t.Category )
      .Include( t => t.Payer )
      .OrderByDescending( t => t.Date )
      .ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<bool> ExistsForUserAsync( long id, long userId ) =>
    await stockTransactionRepository.Query()
      .AnyAsync( t => t.Id == id
                      && t.BrokerAccount.Members.Any( m => m.AppUserId == userId ) ) ;

  /// <inheritdoc />
  public async Task<StockTransaction?> GetByIdForUserAsync( long id, long userId ) =>
    await stockTransactionRepository.Query()
      .Include( t => t.BrokerAccount )
      .Include( t => t.Category )
      .Include( t => t.Payer )
      .FirstOrDefaultAsync( t => t.Id == id
                                 && t.BrokerAccount.Members.Any( m =>
                                   m.AppUserId == userId ) ) ;

  /// <inheritdoc />
  public async Task UpdateAsync( StockTransaction transaction, long userId )
  {
    // Outer null → transaction missing; NewSpendAccountId null → caller not a member of the new
    // broker. Both surface as 404.
    var newBrokerId = transaction.BrokerAccountId ;
    var fetched = await stockTransactionRepository.Query()
      .AsNoTracking()
      .Where( t => t.Id == transaction.Id )
      .Select( t => new
      {
        t.Amount,
        t.BrokerAccountId,
        t.SpendAccountId,
        NewSpendAccountId = brokerRepository.Query()
          .Where( b => b.Id == newBrokerId
                       && b.Members.Any( m => m.AppUserId == userId ) )
          .Select( b => ( long? )b.SpendAccountId )
          .FirstOrDefault()
      } )
      .FirstOrDefaultAsync() ;

    if ( fetched is null )
    {
      throw new KeyNotFoundException( $"StockTransaction {transaction.Id} not found." ) ;
    }

    var newSpendId = fetched.NewSpendAccountId
                     ?? throw new KeyNotFoundException(
                       $"BrokerAccount {newBrokerId} not found for user {userId}." ) ;

    await TransactionCategoryGuard.EnsureKindMatchesAsync(
      categoryRepository, transaction.CategoryId, TransactionKind.Stock ) ;

    var oldDelta = fetched.Amount ;
    var newDelta = transaction.Amount ;
    var oldSpendId = fetched.SpendAccountId ;

    transaction.SpendAccountId = newSpendId ;

    if ( oldSpendId == newSpendId )
    {
      var netDelta = newDelta - oldDelta ;
      if ( netDelta != 0L )
      {
        var spendAccount = await spendRepository.GetByIdOrThrowAsync( oldSpendId ) ;
        spendAccount.Balance += netDelta ;
      }
    }
    else
    {
      var oldSpend = await spendRepository.GetByIdOrThrowAsync( oldSpendId ) ;
      oldSpend.Balance -= oldDelta ;
      var newSpend = await spendRepository.GetByIdOrThrowAsync( newSpendId ) ;
      newSpend.Balance += newDelta ;
    }

    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(
      stockTransactionRepository, oldSpendId, newSpendId ) ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id, long userId )
  {
    var transaction = await GetByIdForUserAsync( id, userId )
                      ?? throw new KeyNotFoundException(
                        $"StockTransaction {id} not found for user {userId}." ) ;
    await SoftDeleteAndReverseAsync( transaction ) ;
  }

  /// <inheritdoc />
  public async Task<List<StockTransaction>> GetAllAsync( long? userId = null, string? symbol = null )
  {
    var query = stockTransactionRepository.Query() ;

    if ( userId is not null )
    {
      query =
        query.Where( t => t.BrokerAccount.Members.Any( m => m.AppUserId == userId.Value ) ) ;
    }

    if ( ! string.IsNullOrWhiteSpace( symbol ) )
    {
      var normalizedSymbol = StockTransaction.NormalizeSymbol( symbol ) ;
      query = query.Where( t => t.Symbol == normalizedSymbol ) ;
    }

    return await query.AsNoTracking()
      .Include( t => t.BrokerAccount )
      .Include( t => t.Category )
      .Include( t => t.Payer )
      .OrderByDescending( t => t.Date )
      .ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<StockTransaction?> GetByIdAsync( long id ) =>
    await stockTransactionRepository.Query()
      .Include( t => t.BrokerAccount )
      .Include( t => t.Category )
      .Include( t => t.Payer )
      .FirstOrDefaultAsync( t => t.Id == id ) ;

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var transaction = await GetByIdAsync( id )
                      ?? throw new KeyNotFoundException( $"StockTransaction {id} not found." ) ;
    await SoftDeleteAndReverseAsync( transaction ) ;
  }

  private async Task SoftDeleteAndReverseAsync( StockTransaction transaction )
  {
    var spendId = transaction.SpendAccountId ;
    var spendAccount = await spendRepository.GetByIdOrThrowAsync( spendId ) ;
    spendAccount.Balance -= transaction.Amount ;
    stockTransactionRepository.SoftDelete( transaction ) ;
    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync( stockTransactionRepository, spendId ) ;
  }

  [GeneratedRegex( @"(\w+):(.+?)(?=\s+\w+:|\s*$)" )]
  private static partial Regex NamedParamRegex() ;

  private async Task<StockTransaction?> ParseBuySellAsync( string text, string[] parts, string typeStr )
  {
    // stock {type} {symbol} {qty} {price} {total} [named params...]
    if ( parts.Length < 6 )
    {
      return null ;
    }

    if ( ! long.TryParse( parts[ 3 ], out var qty )
         || ! long.TryParse( parts[ 4 ], out var price )
         || ! long.TryParse( parts[ 5 ], out var total ) )
    {
      return null ;
    }

    var resolved = await ExtractParamsAndResolveBrokerAsync( text ) ;
    if ( resolved is null )
    {
      return null ;
    }

    var (named, brokerAccountId) = resolved.Value ;
    var stockType = typeStr == "buy" ? StockTransactionType.Buy : StockTransactionType.Sell ;

    return new StockTransaction
    {
      Symbol = parts[ 2 ],
      StockTransactionType = stockType,
      Quantity = qty,
      Price = price,
      Amount = StockTransaction.ComputeAmount( stockType, total ),
      Date = DateTimeUtils.ParseDateOrUtcNow( named.GetValueOrDefault( "date" ) ),
      BrokerAccountId = brokerAccountId,
      Description = named.GetValueOrDefault( "notes", "" )
    } ;
  }

  private async Task<StockTransaction?> ParseDividendAsync( string text, string[] parts )
  {
    // stock div {symbol} {total} [named params...]
    if ( parts.Length < 4 )
    {
      return null ;
    }

    if ( ! long.TryParse( parts[ 3 ], out var total ) )
    {
      return null ;
    }

    var resolved = await ExtractParamsAndResolveBrokerAsync( text ) ;
    if ( resolved is null )
    {
      return null ;
    }

    var (named, brokerAccountId) = resolved.Value ;

    return new StockTransaction
    {
      Symbol = parts[ 2 ],
      StockTransactionType = StockTransactionType.Dividend,
      Quantity = 0,
      Price = 0,
      Amount = StockTransaction.ComputeAmount( StockTransactionType.Dividend, total ),
      Date = DateTimeUtils.ParseDateOrUtcNow( named.GetValueOrDefault( "date" ) ),
      BrokerAccountId = brokerAccountId,
      Description = named.GetValueOrDefault( "notes", "" )
    } ;
  }

  private async Task<(Dictionary<string, string> Named, long BrokerAccountId)?>
    ExtractParamsAndResolveBrokerAsync( string text )
  {
    var named = ExtractNamedParams( text ) ;
    var brokerAccountId = await ResolveBrokerAsync( named.GetValueOrDefault( "broker", "" ) ) ;
    return brokerAccountId is null ? null : ( named, brokerAccountId.Value ) ;
  }

  private async Task<long?> ResolveBrokerAsync( string brokerName )
  {
    var lowered = brokerName.ToLowerInvariant() ;
    return await brokerRepository.Query()
      .Where( b => b.Name.ToLower() == lowered )
      .Select( b => ( long? )b.Id )
      .FirstOrDefaultAsync() ;
  }

  private static Dictionary<string, string> ExtractNamedParams( string text )
  {
    var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase ) ;
    foreach ( Match match in NamedParamRegex().Matches( text ) )
    {
      result[ match.Groups[ 1 ].Value ] = match.Groups[ 2 ].Value ;
    }

    return result ;
  }
}
