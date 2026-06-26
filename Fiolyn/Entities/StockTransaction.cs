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
  ///   Gets or sets the quantity of shares. For Buy/Sell this is the shares traded; for a per-share
  ///   dividend it is the shares the dividend is paid on. Zero for a lump-sum dividend.
  /// </summary>
  [Required]
  public long Quantity { get ; set ; }

  /// <summary>
  ///   Gets or sets the price per share in whole VND. For Buy/Sell this is the trade price; for a
  ///   per-share dividend it is the per-share dividend rate. Zero for a lump-sum dividend.
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

  /// <summary>
  ///   Resolves the unsigned cash total for a trade. A Dividend with a positive
  ///   <paramref name="quantity" /> and <paramref name="price" /> is a per-share dividend
  ///   (total = quantity × price); otherwise the supplied lump <paramref name="totalAmount" /> is
  ///   used. Buy/Sell always use the supplied total (it may include fees, so it is not re-derived
  ///   from quantity × price).
  /// </summary>
  public static long ResolveTradeTotal(
    StockTransactionType type, long quantity, long price, long totalAmount ) =>
    type == StockTransactionType.Dividend && quantity > 0 && price > 0
      ? quantity * price
      : totalAmount ;

  /// <summary>
  ///   Computes the signed <see cref="TransactionBase.Amount" /> from a request's raw fields: resolves
  ///   the unsigned trade total via <see cref="ResolveTradeTotal" /> (per-share vs lump) then signs it
  ///   via <see cref="ComputeAmount" />. The single boundary entry point so callers don't chain the two.
  /// </summary>
  public static long ComputeSignedAmount(
    StockTransactionType type, long quantity, long price, long totalAmount ) =>
    ComputeAmount( type, ResolveTradeTotal( type, quantity, price, totalAmount ) ) ;
}
