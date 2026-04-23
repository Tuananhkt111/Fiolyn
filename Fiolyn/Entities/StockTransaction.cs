using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;
using Fiolyn.Enums ;

namespace Fiolyn.Entities ;

/// <summary>
///   Represents a stock transaction (buy, sell, or dividend) stored in the database.
/// </summary>
public class StockTransaction : IAuditable, ISoftDelete
{
  /// <summary>
  ///   Gets or sets the unique identifier (primary key, auto-increment).
  /// </summary>
  [Key]
  public long Id { get ; set ; }

  /// <summary>
  ///   Gets or sets the ticker symbol, e.g. VNM, AAPL. Auto-normalized to uppercase.
  /// </summary>
  [Required]
  [MaxLength( 20 )]
  public string Symbol
  {
    get ;
    set => field = NormalizeSymbol( value ) ;
  } = string.Empty ;

  /// <summary>
  ///   Gets or sets the transaction type (Buy, Sell, or Dividend).
  /// </summary>
  [Required]
  public TransactionType TransactionType { get ; set ; }

  /// <summary>
  ///   Gets or sets the quantity of shares traded. Zero for dividends.
  /// </summary>
  [Required]
  public long Quantity { get ; set ; }

  /// <summary>
  ///   Gets or sets the price per share in whole VND. Zero for dividends.
  /// </summary>
  [Required]
  public long Price { get ; set ; }

  /// <summary>
  ///   Gets or sets the total cash amount in whole VND, including fees.
  /// </summary>
  [Required]
  public long TotalAmount { get ; set ; }

  /// <summary>
  ///   Gets or sets the UTC date of the trade.
  /// </summary>
  [Required]
  public DateTime TransactionDate { get ; set ; }

  /// <summary>
  ///   Gets or sets the foreign key to the associated broker account.
  /// </summary>
  [Required]
  public long BrokerAccountId { get ; set ; }

  /// <summary>
  ///   Gets or sets the associated broker account.
  /// </summary>
  public BrokerAccount BrokerAccount { get ; set ; } = null! ;

  /// <summary>
  ///   Gets or sets optional free-text notes.
  /// </summary>
  [MaxLength( 500 )]
  public string Notes { get ; set ; } = string.Empty ;

  /// <summary>
  ///   Gets or sets the UTC timestamp when this record was created.
  /// </summary>
  [Required]
  public DateTime Created { get ; set ; }

  /// <summary>
  ///   Gets or sets the UTC timestamp when this record was last modified.
  /// </summary>
  [Required]
  public DateTime LastModified { get ; set ; }

  /// <summary>
  ///   Gets or sets a value indicating whether this record has been soft-deleted.
  /// </summary>
  [Required]
  public bool IsDeleted { get ; set ; }

  /// <summary>
  ///   Normalizes a stock ticker symbol to uppercase.
  /// </summary>
  public static string NormalizeSymbol( string symbol ) => symbol.ToUpperInvariant() ;
}
