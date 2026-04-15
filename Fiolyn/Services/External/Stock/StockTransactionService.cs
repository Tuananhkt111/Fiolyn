using System.Text.RegularExpressions ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Utils ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.External.Stock ;

/// <summary>
///   Manages stock transactions and parses Telegram message format.
/// </summary>
public partial class StockTransactionService(
  IRepository<StockTransaction> repository,
  ILogger<StockTransactionService> logger ) : IStockTransactionService
{
  /// <inheritdoc />
  public StockTransaction? ParseMessage( string text )
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
        "buy" or "sell" => ParseBuySell( text, parts, typeStr ),
        "div" => ParseDividend( text, parts ),
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
    await repository.AddAsync( transaction ) ;
    await repository.SaveAsync() ;
    return transaction ;
  }

  /// <inheritdoc />
  public async Task<List<StockTransaction>> GetAllAsync( string? symbol = null )
  {
    var query = repository.QueryActive() ;

    if ( ! string.IsNullOrWhiteSpace( symbol ) )
    {
      query = query.Where( t => t.Symbol == symbol.ToUpperInvariant() ) ;
    }

    return await query.OrderByDescending( t => t.TransactionDate ).ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<StockTransaction?> GetByIdAsync( long id )
  {
    var transaction = await repository.GetByIdAsync( id ) ;
    if ( transaction is { IsDeleted: true } )
    {
      return null ;
    }

    return transaction ;
  }

  /// <inheritdoc />
  public async Task UpdateAsync( StockTransaction transaction )
  {
    repository.Update( transaction ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var transaction = await repository.GetByIdAsync( id )
                      ?? throw new KeyNotFoundException( $"StockTransaction {id} not found" ) ;
    repository.SoftDelete( transaction ) ;
    await repository.SaveAsync() ;
  }

  [GeneratedRegex( @"\[(\w+):([^\]]*)\]" )]
  private static partial Regex NamedParamRegex() ;

  private static StockTransaction? ParseBuySell( string text, string[] parts, string typeStr )
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

    return new StockTransaction
    {
      Symbol = parts[ 2 ].ToUpperInvariant(),
      TransactionType = typeStr == "buy" ? TransactionType.Buy : TransactionType.Sell,
      Quantity = qty,
      Price = price,
      TotalAmount = total,
      TransactionDate = DateTimeUtils.ParseDateOrUtcNow( named.GetValueOrDefault( "date" ) ),
      Broker = named.GetValueOrDefault( "broker", "" ),
      Notes = named.GetValueOrDefault( "notes", "" )
    } ;
  }

  private static StockTransaction? ParseDividend( string text, string[] parts )
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

    return new StockTransaction
    {
      Symbol = parts[ 2 ].ToUpperInvariant(),
      TransactionType = TransactionType.Dividend,
      Quantity = 0,
      Price = 0,
      TotalAmount = total,
      TransactionDate = DateTimeUtils.ParseDateOrUtcNow( named.GetValueOrDefault( "date" ) ),
      Broker = named.GetValueOrDefault( "broker", "" ),
      Notes = named.GetValueOrDefault( "notes", "" )
    } ;
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