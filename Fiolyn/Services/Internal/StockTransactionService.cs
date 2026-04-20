using System.Text.RegularExpressions ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Utils ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Manages stock transactions and parses Telegram message format.
/// </summary>
public partial class StockTransactionService(
  IRepository<StockTransaction> stockTransactionRepository,
  IRepository<BrokerAccount> brokerRepository,
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
  public async Task<StockTransaction> CreateAsync( StockTransaction transaction )
  {
    await stockTransactionRepository.AddAsync( transaction ) ;
    await stockTransactionRepository.SaveAsync() ;
    return transaction ;
  }

  /// <inheritdoc />
  public async Task<List<StockTransaction>> GetAllAsync( string? symbol = null )
  {
    var query = stockTransactionRepository.QueryActive() ;

    if ( ! string.IsNullOrWhiteSpace( symbol ) )
    {
      var normalizedSymbol = StockTransaction.NormalizeSymbol( symbol ) ;
      query = query.Where( t => t.Symbol == normalizedSymbol ) ;
    }

    return await query.AsNoTracking().Include( t => t.BrokerAccount ).OrderByDescending( t => t.TransactionDate ).ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<bool> ExistsAsync( long id )
    => await stockTransactionRepository.QueryActive().AnyAsync( t => t.Id == id ) ;

  /// <inheritdoc />
  public async Task<StockTransaction?> GetByIdAsync( long id )
    => await stockTransactionRepository.QueryActive()
      .Include( t => t.BrokerAccount )
      .FirstOrDefaultAsync( t => t.Id == id ) ;

  /// <inheritdoc />
  public async Task UpdateAsync( StockTransaction transaction )
  {
    stockTransactionRepository.Update( transaction ) ;
    await stockTransactionRepository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var transaction = await stockTransactionRepository.GetActiveByIdOrThrowAsync( id ) ;
    stockTransactionRepository.SoftDelete( transaction ) ;
    await stockTransactionRepository.SaveAsync() ;
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

    if ( ! decimal.TryParse( parts[ 3 ], out var qty )
         || ! decimal.TryParse( parts[ 4 ], out var price )
         || ! decimal.TryParse( parts[ 5 ], out var total ) )
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

    if ( ! decimal.TryParse( parts[ 3 ], out var total ) )
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

  private async Task<(Dictionary<string, string> Named, long BrokerAccountId)?> ExtractParamsAndResolveBrokerAsync( string text )
  {
    var named = ExtractNamedParams( text ) ;
    var brokerAccountId = await ResolveBrokerAsync( named.GetValueOrDefault( "broker", "" ) ) ;
    return brokerAccountId is null ? null : (named, brokerAccountId.Value) ;
  }

  private async Task<long?> ResolveBrokerAsync( string brokerName )
  {
    var broker = await brokerRepository.QueryActive()
      .FirstOrDefaultAsync( b => b.Name.ToLower() == brokerName.ToLower() ) ;
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