using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Entities ;

/// <summary>
///   Represents a refresh token issued to a user. Revocation is explicit via RevokedAt
///   (no soft-delete — tokens are never restored).
/// </summary>
[Index( nameof( TokenHash ) )]
public class RefreshToken : IAuditable
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
  public long UserId { get ; set ; }

  /// <summary>
  ///   Gets or sets the owning user.
  /// </summary>
  public AppUser User { get ; set ; } = null! ;

  /// <summary>
  ///   Gets or sets the SHA-256 hash of the raw refresh token. The raw token is never persisted.
  /// </summary>
  [Required]
  [MaxLength( 128 )]
  public string TokenHash { get ; set ; } = string.Empty ;

  /// <summary>
  ///   Gets or sets the UTC expiry timestamp.
  /// </summary>
  [Required]
  public DateTime ExpiresAt { get ; set ; }

  /// <summary>
  ///   Gets or sets the UTC timestamp when this token was revoked (logout, rotation, or reuse).
  ///   Null while the token is still valid.
  /// </summary>
  public DateTime? RevokedAt { get ; set ; }

  /// <summary>
  ///   Gets or sets the hash of the token that replaced this one via rotation. Used for
  ///   reuse-detection — replaying a revoked token revokes the entire rotation chain.
  /// </summary>
  [MaxLength( 128 )]
  public string? ReplacedByTokenHash { get ; set ; }

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
}