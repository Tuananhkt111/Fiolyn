using System.ComponentModel.DataAnnotations ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Entities ;

/// <summary>
///   Join entity linking a <see cref="BrokerAccount" /> to an <see cref="AppUser" /> member.
///   All members are equal: any member can read, edit, delete the account, manage membership, and
///   CRUD every <see cref="StockTransaction" /> on it. Membership is immutable presence — join = insert,
///   leave = hard delete. No soft-delete, no audit.
/// </summary>
[PrimaryKey( nameof( BrokerAccountId ), nameof( AppUserId ) )]
[Index( nameof( AppUserId ), nameof( BrokerAccountId ) )]
public class BrokerAccountMember
{
  /// <summary>
  ///   Gets or sets the foreign key to the broker account.
  /// </summary>
  [Required]
  public long BrokerAccountId { get ; set ; }

  /// <summary>
  ///   Gets or sets the broker account navigation.
  /// </summary>
  public BrokerAccount BrokerAccount { get ; set ; } = null! ;

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
