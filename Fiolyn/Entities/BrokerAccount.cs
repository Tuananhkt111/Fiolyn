using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;

namespace Fiolyn.Entities ;

/// <summary>
///   Represents a broker or trading account used for stock transactions.
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
  public string Name { get ; set ; } = "" ;

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