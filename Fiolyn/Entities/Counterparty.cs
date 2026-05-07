using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Entities ;

/// <summary>
///   A person (or organisation) the owning <see cref="AppUser" /> has lent money to or borrowed money
///   from. Each row is private to its owner — counterparties are not shared across SpendAccount
///   members. The display name is auto-trimmed; case-insensitive uniqueness per owner is enforced by
///   a Postgres expression index <c>(AppUserId, LOWER(Name))</c> created in the migration.
/// </summary>
[Index( nameof( AppUserId ) )]
public class Counterparty : IAuditable, ISoftDelete
{
  /// <summary>
  ///   Gets or sets the unique identifier (primary key, auto-increment).
  /// </summary>
  [Key]
  public long Id { get ; set ; }

  /// <summary>
  ///   Gets or sets the foreign key to the <see cref="AppUser" /> who owns this counterparty.
  /// </summary>
  [Required]
  public long AppUserId { get ; set ; }

  /// <summary>
  ///   Gets or sets the owning user.
  /// </summary>
  public AppUser AppUser { get ; set ; } = null! ;

  /// <summary>
  ///   Gets or sets the counterparty's display name. Auto-trimmed.
  /// </summary>
  [Required]
  [MaxLength( 100 )]
  public string Name
  {
    get ;
    set => field = value.Trim() ;
  } = string.Empty ;

  [Required]
  public DateTime Created { get ; set ; }

  [Required]
  public DateTime LastModified { get ; set ; }

  [Required]
  public bool IsDeleted { get ; set ; }
}
