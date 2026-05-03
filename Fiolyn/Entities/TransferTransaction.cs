using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Entities ;

/// <summary>
///   Move money between two spend accounts the caller is a member of. The base <c>SpendAccountId</c>
///   is the source (debited by <c>-Amount</c>); <see cref="DestinationSpendAccountId" /> is the
///   destination (credited by <c>+Amount</c>). Both legs land in one atomic save.
/// </summary>
public class TransferTransaction : TransactionBase
{
  /// <summary>
  ///   Gets or sets the foreign key to the destination spend account.
  /// </summary>
  [Required]
  public long DestinationSpendAccountId { get ; set ; }

  /// <summary>
  ///   Gets or sets the destination spend account.
  /// </summary>
  public SpendAccount DestinationSpendAccount { get ; set ; } = null! ;
}
