using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Entities ;

/// <summary>
///   Money borrowed from a counterparty — money in (positive <c>Amount</c>) but owed. Counterparty
///   is free-text since most are not Fiolyn users.
/// </summary>
public class BorrowingTransaction : TransactionBase
{
  /// <summary>
  ///   Gets or sets the name of the counterparty the money was borrowed from.
  /// </summary>
  [Required]
  [MaxLength( 100 )]
  public string CounterpartyName { get ; set ; } = string.Empty ;

  /// <summary>
  ///   Gets or sets the optional UTC date the borrowing is due.
  /// </summary>
  public DateTime? DueDate { get ; set ; }
}
