using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Entities ;

/// <summary>
///   Money lent to a counterparty — money out (negative <c>Amount</c>) but recoverable. The
///   counterparty is a per-user <see cref="Counterparty" /> row; the leaf service resolves the
///   request's counterparty name to (or creates) a row owned by the caller.
/// </summary>
public class LendTransaction : TransactionBase, IHasCounterparty
{
  /// <summary>
  ///   Gets or sets the optional UTC date the loan is due.
  /// </summary>
  public DateTime? DueDate { get ; set ; }

  /// <summary>
  ///   Gets or sets the foreign key to the <see cref="Entities.Counterparty" /> this loan is owed by.
  /// </summary>
  [Required]
  public long CounterpartyId { get ; set ; }

  /// <summary>
  ///   Gets or sets the resolved counterparty.
  /// </summary>
  public Counterparty Counterparty { get ; set ; } = null! ;
}
