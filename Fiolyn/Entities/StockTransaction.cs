using System.ComponentModel.DataAnnotations ;
using Fiolyn.Enums ;

namespace Fiolyn.Entities ;

/// <summary>
///   A stock transaction (buy, sell, or dividend). Inherits the common transaction columns
///   (<c>Date</c>, <c>Description</c>, <c>SpendAccountId</c>, <c>Amount</c>, <c>PayerId</c>,
///   <c>CategoryId</c>, audit, soft-delete) from <see cref="TransactionBase" />. The signed
///   <c>Amount</c> is computed at the boundary via <see cref="ComputeAmount" /> from
///   <c>StockTransactionType</c> + an unsigned trade total: Buy → negative, Sell/Dividend → positive.
///   The unsigned trade total is therefore <c>Math.Abs(Amount)</c>.
/// </summary>
public class StockTransaction : TransactionBase
{
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
  ///   Gets or sets the stock-specific transaction type (Buy, Sell, or Dividend).
  /// </summary>
  [Required]
  public StockTransactionType StockTransactionType { get ; set ; }

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
  ///   Gets or sets the foreign key to the associated broker account.
  /// </summary>
  [Required]
  public long BrokerAccountId { get ; set ; }

  /// <summary>
  ///   Gets or sets the associated broker account.
  /// </summary>
  public BrokerAccount BrokerAccount { get ; set ; } = null! ;

  /// <summary>
  ///   Normalizes a stock ticker symbol to uppercase.
  /// </summary>
  public static string NormalizeSymbol( string symbol ) => symbol.ToUpperInvariant() ;

  /// <summary>
  ///   Computes the signed <see cref="TransactionBase.Amount" /> for a stock trade from its type and
  ///   unsigned cash total. Buy → negative; Sell, Dividend → positive.
  /// </summary>
  public static long ComputeAmount( StockTransactionType type, long totalAmount ) =>
    type switch
    {
      StockTransactionType.Buy => -totalAmount,
      StockTransactionType.Sell => totalAmount,
      StockTransactionType.Dividend => totalAmount,
      _ => 0L
    } ;
}
