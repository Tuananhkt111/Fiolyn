using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;

namespace Fiolyn.Entities ;

/// <summary>
///   Represents a Telegram user's information stored in the database.
/// </summary>
/// <remarks>
///   Tracks Telegram user identity and audit information for access control and audit trails.
///   Implements soft-delete and auditable patterns for data integrity.
/// </remarks>
public class TelegramUserInfo : ISoftDelete, IAuditable
{
  /// <summary>
  ///   Gets or sets the unique Telegram user ID (primary key).
  /// </summary>
  [Key]
  public long TelegramId { get ; set ; }

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