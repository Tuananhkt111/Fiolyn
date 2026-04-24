using System.ComponentModel.DataAnnotations ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Entities ;

/// <summary>
///   Join entity linking a <see cref="SpendAccount" /> to an <see cref="AppUser" /> member.
///   All members are equal: any member can read, edit, delete the account, and manage membership.
///   Membership is immutable presence — join = insert, leave = hard delete. No soft-delete, no audit.
/// </summary>
[PrimaryKey( nameof( SpendAccountId ), nameof( AppUserId ) )]
[Index( nameof( AppUserId ), nameof( SpendAccountId ) )]
public class SpendAccountMember
{
  /// <summary>
  ///   Gets or sets the foreign key to the spend account.
  /// </summary>
  [Required]
  public long SpendAccountId { get ; set ; }

  /// <summary>
  ///   Gets or sets the spend account navigation.
  /// </summary>
  public SpendAccount SpendAccount { get ; set ; } = null! ;

  /// <summary>
  ///   Gets or sets the foreign key to the member user.
  /// </summary>
  [Required]
  public long AppUserId { get ; set ; }

  /// <summary>
  ///   Gets or sets the member user navigation.
  /// </summary>
  public AppUser AppUser { get ; set ; } = null! ;

  /// <summary>
  ///   Gets or sets the UTC timestamp when this user joined the account.
  /// </summary>
  [Required]
  public DateTime JoinedAt { get ; set ; }
}
