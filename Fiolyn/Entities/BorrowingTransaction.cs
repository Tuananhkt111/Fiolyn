using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Entities ;

/// <summary>
///   Money borrowed from a counterparty — money in (positive <c>Amount</c>) but owed. The
///   counterparty is a per-user <see cref="Counterparty" /> row; the leaf service resolves the
///   request's counterparty name to (or creates) a row owned by the caller.
/// </summary>
public class BorrowingTransaction : TransactionBase, IHasCounterparty
{
  /// <summary>
  ///   Gets or sets the optional UTC date the borrowing is due.
  /// </summary>
  public DateTime? DueDate { get ; set ; }

  /// <summary>
  ///   Gets or sets the foreign key to the <see cref="Entities.Counterparty" /> this debt is owed to.
  /// </summary>
  [Required]
  public long CounterpartyId { get ; set ; }

  /// <summary>
  ///   Gets or sets the resolved counterparty.
  /// </summary>
  public Counterparty Counterparty { get ; set ; } = null! ;
}
