using System.Text.RegularExpressions ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Utils ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Manages stock transactions and parses Telegram message format. User-scoped methods enforce
///   that the caller is a member of the transaction's parent <see cref="BrokerAccount" /> — not a
///   member → 404 (via <see cref="KeyNotFoundException" />).
///   Every persisted transaction adjusts the broker's linked <see cref="SpendAccount" />.<c>Balance</c>:
///   Buy → <c>-TotalAmount</c>; Sell, Dividend → <c>+TotalAmount</c>. Updates reverse the old delta and
///   apply the new; soft-deletes reverse. Balance writes go through
///   <see cref="ISpendAccountBalanceWriter" /> (atomic SQL <c>Balance = Balance + @delta</c> via
///   <c>ExecuteUpdate</c>, so two concurrent writers cannot lose an update). Atomicity across the
///   balance write and the transaction insert/update is enforced by wrapping both in
///   <see cref="IUnitOfWork.ExecuteInTransactionAsync" /> — the writer's <c>ExecuteUpdate</c> runs
///   outside <c>SaveChanges</c>, so the wrapping transaction is required.
/// </summary>
public partial class StockTransactionService(
  IRepository<StockTransaction> stockTransactionRepository,
  IRepository<BrokerAccount> brokerRepository,
  ISpendAccountBalanceWriter spendBalanceWriter,
  IUnitOfWork unitOfWork,
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
    var spendId = await brokerRepository.QueryActive()
                    .Where( b => b.Id == transaction.BrokerAccountId
                                 && b.Members.Any( m => m.AppUserId == userId && ! m.AppUser.IsDeleted ) )
                    .Select( b => ( long? )b.SpendAccountId )
                    .FirstOrDefaultAsync()
                  ?? throw new KeyNotFoundException(
                    $"BrokerAccount {transaction.BrokerAccountId} not found for user {userId}." ) ;

    var delta = ComputeBalanceDelta( transaction.TransactionType, transaction.TotalAmount ) ;

    await unitOfWork.ExecuteInTransactionAsync( async () =>
    {
      await spendBalanceWriter.ApplyDeltaAsync( spendId, delta ) ;
      await stockTransactionRepository.AddAsync( transaction ) ;
      await stockTransactionRepository.SaveAsync() ;
    } ) ;

    return transaction ;
  }

  /// <inheritdoc />
  public async Task<List<StockTransaction>> GetAllForUserAsync( long userId, string? symbol = null )
  {
    var query = stockTransactionRepository.QueryActive()
      .Where( t => t.BrokerAccount.Members.Any( m => m.AppUserId == userId && ! m.AppUser.IsDeleted ) ) ;

    if ( ! string.IsNullOrWhiteSpace( symbol ) )
    {
      var normalizedSymbol = StockTransaction.NormalizeSymbol( symbol ) ;
      query = query.Where( t => t.Symbol == normalizedSymbol ) ;
    }

    return await query.AsNoTracking().Include( t => t.BrokerAccount )
      .OrderByDescending( t => t.TransactionDate )
      .ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<bool> ExistsForUserAsync( long id, long userId ) =>
    await stockTransactionRepository.QueryActive()
      .AnyAsync( t => t.Id == id
                      && t.BrokerAccount.Members.Any( m => m.AppUserId == userId && ! m.AppUser.IsDeleted ) ) ;

  /// <inheritdoc />
  public async Task<StockTransaction?> GetByIdForUserAsync( long id, long userId ) =>
    await stockTransactionRepository.QueryActive()
      .Include( t => t.BrokerAccount )
      .FirstOrDefaultAsync( t => t.Id == id
                                 && t.BrokerAccount.Members.Any( m =>
                                   m.AppUserId == userId && ! m.AppUser.IsDeleted ) ) ;

  /// <inheritdoc />
  public async Task UpdateAsync( StockTransaction transaction, long userId )
  {
    // Outer null → transaction missing; NewSpendAccountId null → caller not a member of the new
    // broker. Both surface as 404.
    var newBrokerId = transaction.BrokerAccountId ;
    var fetched = await stockTransactionRepository.QueryActive()
      .AsNoTracking()
      .Where( t => t.Id == transaction.Id )
      .Select( t => new
      {
        t.TransactionType,
        t.TotalAmount,
        t.BrokerAccountId,
        OldSpendAccountId = t.BrokerAccount.SpendAccountId,
        NewSpendAccountId = brokerRepository.QueryActive()
          .Where( b => b.Id == newBrokerId
                       && b.Members.Any( m => m.AppUserId == userId && ! m.AppUser.IsDeleted ) )
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

    var oldDelta = ComputeBalanceDelta( fetched.TransactionType, fetched.TotalAmount ) ;
    var newDelta = ComputeBalanceDelta( transaction.TransactionType, transaction.TotalAmount ) ;
    var oldSpendId = fetched.OldSpendAccountId ;

    await unitOfWork.ExecuteInTransactionAsync( async () =>
    {
      if ( oldSpendId == newSpendId )
      {
        await spendBalanceWriter.ApplyDeltaAsync( newSpendId, newDelta - oldDelta ) ;
      }
      else
      {
        await spendBalanceWriter.ApplyDeltaAsync( oldSpendId, -oldDelta ) ;
        await spendBalanceWriter.ApplyDeltaAsync( newSpendId, newDelta ) ;
      }

      stockTransactionRepository.Update( transaction ) ;
      await stockTransactionRepository.SaveAsync() ;
    } ) ;
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
    var query = stockTransactionRepository.QueryActive() ;

    if ( userId is not null )
    {
      query =
        query.Where( t => t.BrokerAccount.Members.Any( m => m.AppUserId == userId.Value && ! m.AppUser.IsDeleted ) ) ;
    }

    if ( ! string.IsNullOrWhiteSpace( symbol ) )
    {
      var normalizedSymbol = StockTransaction.NormalizeSymbol( symbol ) ;
      query = query.Where( t => t.Symbol == normalizedSymbol ) ;
    }

    return await query.AsNoTracking().Include( t => t.BrokerAccount )
      .OrderByDescending( t => t.TransactionDate )
      .ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<StockTransaction?> GetByIdAsync( long id ) =>
    await stockTransactionRepository.QueryActive()
      .Include( t => t.BrokerAccount )
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
    var reversal = -ComputeBalanceDelta( transaction.TransactionType, transaction.TotalAmount ) ;
    await unitOfWork.ExecuteInTransactionAsync( async () =>
    {
      await spendBalanceWriter.ApplyDeltaAsync( transaction.BrokerAccount.SpendAccountId, reversal ) ;
      stockTransactionRepository.SoftDelete( transaction ) ;
      await stockTransactionRepository.SaveAsync() ;
    } ) ;
  }

  private static long ComputeBalanceDelta( TransactionType type, long totalAmount ) =>
    type switch
    {
      TransactionType.Buy => -totalAmount,
      TransactionType.Sell => totalAmount,
      TransactionType.Dividend => totalAmount,
      _ => 0L
    } ;

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

    return new StockTransaction
    {
      Symbol = parts[ 2 ],
      TransactionType = typeStr == "buy" ? TransactionType.Buy : TransactionType.Sell,
      Quantity = qty,
      Price = price,
      TotalAmount = total,
      TransactionDate = DateTimeUtils.ParseDateOrUtcNow( named.GetValueOrDefault( "date" ) ),
      BrokerAccountId = brokerAccountId,
      Notes = named.GetValueOrDefault( "notes", "" )
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
      TransactionType = TransactionType.Dividend,
      Quantity = 0,
      Price = 0,
      TotalAmount = total,
      TransactionDate = DateTimeUtils.ParseDateOrUtcNow( named.GetValueOrDefault( "date" ) ),
      BrokerAccountId = brokerAccountId,
      Notes = named.GetValueOrDefault( "notes", "" )
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
    var broker = await brokerRepository.QueryActive()
      .FirstOrDefaultAsync( b => b.Name.ToLower() == lowered ) ;
    return broker?.Id ;
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
