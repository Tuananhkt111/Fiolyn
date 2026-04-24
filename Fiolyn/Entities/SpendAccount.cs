using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;
using Fiolyn.Enums ;

namespace Fiolyn.Entities ;

/// <summary>
///   Represents a "spend account" that stores a balance, e.g. a bank account or credit card.
///   Shared across one or more <see cref="AppUser" />s via <see cref="SpendAccountMember" /> — every
///   member has equal read/write/manage rights.
/// </summary>
public class SpendAccount : IAuditable, ISoftDelete
{
  /// <summary>
  ///   Gets or sets the unique identifier (primary key, auto-increment).
  /// </summary>
  [Key]
  public long Id { get ; set ; }

  /// <summary>
  ///   Gets or sets the display name, e.g. "VCB Savings" or "Visa Platinum".
  /// </summary>
  [Required]
  [MaxLength( 100 )]
  public string Name { get ; set ; } = string.Empty ;

  /// <summary>
  ///   Gets or sets the account type (Cash or Credit).
  /// </summary>
  [Required]
  public SpendAccountType Type { get ; set ; }

  /// <summary>
  ///   Gets or sets the current balance in whole VND. Signed — may be negative for credit accounts.
  /// </summary>
  [Required]
  public long Balance { get ; set ; }

  /// <summary>
  ///   Gets or sets the users who share this account. Treated as an access-control set: a user is a
  ///   member iff a matching <see cref="SpendAccountMember" /> row exists.
  /// </summary>
  public ICollection<SpendAccountMember> Members { get ; set ; } = [] ;

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
}
