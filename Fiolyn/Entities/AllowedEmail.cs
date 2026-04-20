using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Entities ;

/// <summary>
///   Represents an email address authorized to sign in via Google SSO.
/// </summary>
[Index( nameof( Email ), IsUnique = true )]
public class AllowedEmail : IAuditable, ISoftDelete
{
  /// <summary>
  ///   Gets or sets the unique identifier (primary key, auto-increment).
  /// </summary>
  [Key]
  public long Id { get ; set ; }

  /// <summary>
  ///   Gets or sets the whitelisted email address. Auto-normalized to lowercase.
  /// </summary>
  [Required]
  [MaxLength( 256 )]
  public string Email
  {
    get ;
    set => field = AppUser.NormalizeEmail( value ) ;
  } = string.Empty ;

  /// <summary>
  ///   Gets or sets a free-text note explaining the invitation.
  /// </summary>
  [MaxLength( 500 )]
  public string? Note { get ; set ; }

  /// <summary>
  ///   Gets or sets the ID of the admin user who invited this email.
  ///   Null for entries seeded from configuration.
  /// </summary>
  public long? InvitedByUserId { get ; set ; }

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