using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Entities ;

/// <summary>
///   Money lent to a counterparty — money out (negative <c>Amount</c>) but recoverable. Counterparty
///   is free-text since most are not Fiolyn users.
/// </summary>
public class LendTransaction : TransactionBase
{
  /// <summary>
  ///   Gets or sets the name of the counterparty the money was lent to.
  /// </summary>
  [Required]
  [MaxLength( 100 )]
  public string CounterpartyName { get ; set ; } = string.Empty ;

  /// <summary>
  ///   Gets or sets the optional UTC date the loan is due.
  /// </summary>
  public DateTime? DueDate { get ; set ; }
}
