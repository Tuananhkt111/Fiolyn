using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;
using Fiolyn.Enums ;

namespace Fiolyn.Entities ;

/// <summary>
///   Represents a user-owned "spend account" that stores a balance, e.g. a bank account or credit card.
/// </summary>
public class SpendAccount : IAuditable, ISoftDelete
{
  /// <summary>
  ///   Gets or sets the unique identifier (primary key, auto-increment).
  /// </summary>
  [Key]
  public long Id { get ; set ; }

  /// <summary>
  ///   Gets or sets the foreign key to the owning user.
  /// </summary>
  [Required]
  public long AppUserId { get ; set ; }

  /// <summary>
  ///   Gets or sets the owning user navigation.
  /// </summary>
  public AppUser AppUser { get ; set ; } = null! ;

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
