using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Entities ;

/// <summary>
///   Represents a human user authenticated via Google SSO.
/// </summary>
[Index( nameof( Email ), IsUnique = true )]
[Index( nameof( GoogleSub ), IsUnique = true )]
public class AppUser : IAuditable, ISoftDelete
{
  /// <summary>
  ///   Gets or sets the unique identifier (primary key, auto-increment).
  /// </summary>
  [Key]
  public long Id { get ; set ; }

  private string _email = "" ;

  /// <summary>
  ///   Gets or sets the user's email address. Auto-normalized to lowercase.
  /// </summary>
  [Required]
  [MaxLength( 256 )]
  public string Email
  {
    get => _email ;
    set => _email = NormalizeEmail( value ) ;
  }

  /// <summary>
  ///   Gets or sets the stable Google account ID (ID token `sub` claim).
  /// </summary>
  [Required]
  [MaxLength( 64 )]
  public string GoogleSub { get ; set ; } = string.Empty ;

  /// <summary>
  ///   Gets or sets the display name from Google. Nullable.
  /// </summary>
  [MaxLength( 256 )]
  public string? DisplayName { get ; set ; }

  /// <summary>
  ///   Gets or sets the profile picture URL from Google. Nullable.
  /// </summary>
  [MaxLength( 1024 )]
  public string? PictureUrl { get ; set ; }

  /// <summary>
  ///   Gets or sets a value indicating whether this user has administrative privileges.
  /// </summary>
  [Required]
  public bool IsAdmin { get ; set ; }

  /// <summary>
  ///   Gets or sets the UTC timestamp of the last successful login.
  /// </summary>
  public DateTime? LastLoginAt { get ; set ; }

  /// <summary>
  ///   Gets or sets the refresh tokens issued to this user.
  /// </summary>
  public ICollection<RefreshToken> RefreshTokens { get ; set ; } = new List<RefreshToken>() ;

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

  /// <summary>
  ///   Normalizes an email address to lowercase.
  /// </summary>
  public static string NormalizeEmail( string email ) => email.Trim().ToLowerInvariant() ;
}