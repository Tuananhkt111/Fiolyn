using System.Text.RegularExpressions ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
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
      query = query.Where( t => t.Symbol == symbol.ToUpperInvariant() ) ;
    }

    return await query.OrderByDescending( t => t.TransactionDate ).ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<StockTransaction?> GetByIdAsync( long id )
  {
    var transaction = await stockTransactionRepository.GetByIdAsync( id ) ;
    if ( transaction is { IsDeleted: true } )
    {
      return null ;
    }

    return transaction ;
  }

  /// <inheritdoc />
  public async Task UpdateAsync( StockTransaction transaction )
  {
    stockTransactionRepository.Update( transaction ) ;
    await stockTransactionRepository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var transaction = await stockTransactionRepository.GetByIdAsync( id )
                      ?? throw new KeyNotFoundException( $"StockTransaction {id} not found" ) ;
    stockTransactionRepository.SoftDelete( transaction ) ;
    await stockTransactionRepository.SaveAsync() ;
  }

  [GeneratedRegex( @"\[(\w+):([^\]]*)\]" )]
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

    var named = ExtractNamedParams( text ) ;
    var brokerAccountId = await ResolveBrokerAsync( named.GetValueOrDefault( "broker", "" ) ) ;
    if ( brokerAccountId is null )
    {
      return null ;
    }

    return new StockTransaction
    {
      Symbol = parts[ 2 ].ToUpperInvariant(),
      TransactionType = typeStr == "buy" ? TransactionType.Buy : TransactionType.Sell,
      Quantity = qty,
      Price = price,
      TotalAmount = total,
      TransactionDate = DateTimeUtils.ParseDateOrUtcNow( named.GetValueOrDefault( "date" ) ),
      BrokerAccountId = brokerAccountId.Value,
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

    var named = ExtractNamedParams( text ) ;
    var brokerAccountId = await ResolveBrokerAsync( named.GetValueOrDefault( "broker", "" ) ) ;
    if ( brokerAccountId is null )
    {
      return null ;
    }

    return new StockTransaction
    {
      Symbol = parts[ 2 ].ToUpperInvariant(),
      TransactionType = TransactionType.Dividend,
      Quantity = 0,
      Price = 0,
      TotalAmount = total,
      TransactionDate = DateTimeUtils.ParseDateOrUtcNow( named.GetValueOrDefault( "date" ) ),
      BrokerAccountId = brokerAccountId.Value,
      Notes = named.GetValueOrDefault( "notes", "" )
    } ;
  }

  private async Task<long?> ResolveBrokerAsync( string brokerName )
  {
    var broker = await brokerRepository.QueryActive()
      .FirstOrDefaultAsync( b => b.Name == brokerName ) ;
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