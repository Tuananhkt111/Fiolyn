using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;

namespace Fiolyn.Entities ;

/// <summary>
///   Represents a broker or trading account used for stock transactions.
///   Shared across one or more <see cref="AppUser" />s via <see cref="BrokerAccountMember" /> — every
///   member has equal read/write/manage rights, including over the account's
///   <see cref="StockTransaction" /> children.
///   Each broker is linked to exactly one <see cref="SpendAccount" />: stock transactions on the broker
///   automatically debit (Buy) or credit (Sell, Dividend) <c>SpendAccount.Balance</c> by their
///   <c>TotalAmount</c>. The link is set at create time and may be changed later by a member who is
///   also a member of the target spend account.
/// </summary>
public class BrokerAccount : IAuditable, ISoftDelete
{
  /// <summary>
  ///   Gets or sets the unique identifier (primary key, auto-increment).
  /// </summary>
  [Key]
  public long Id { get ; set ; }

  /// <summary>
  ///   Gets or sets the broker or account name, e.g. VPS, TCBS.
  /// </summary>
  [Required]
  [MaxLength( 100 )]
  public string Name { get ; set ; } = string.Empty ;

  /// <summary>
  ///   Gets or sets the foreign key to the <see cref="SpendAccount" /> whose balance is auto-adjusted
  ///   by every stock transaction on this broker.
  /// </summary>
  [Required]
  public long SpendAccountId { get ; set ; }

  /// <summary>
  ///   Gets or sets the linked spend account. <c>OnDelete: Restrict</c> is configured in
  ///   <see cref="Fiolyn.Data.AppDbContext.OnModelCreating" />.
  /// </summary>
  public SpendAccount SpendAccount { get ; set ; } = null! ;

  /// <summary>
  ///   Gets or sets the users who share this account. Treated as an access-control set: a user is a
  ///   member iff a matching <see cref="BrokerAccountMember" /> row exists.
  /// </summary>
  public ICollection<BrokerAccountMember> Members { get ; set ; } = [] ;

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
