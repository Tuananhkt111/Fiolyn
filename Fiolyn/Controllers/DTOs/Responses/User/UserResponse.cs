using Fiolyn.Entities ;

namespace Fiolyn.Controllers.DTOs.Responses.User ;

/// <summary>
///   Response body for user profile endpoints.
/// </summary>
public record UserResponse(
  long Id,
  string Email,
  string? DisplayName,
  string? PictureUrl,
  bool IsAdmin,
  DateTime? LastLoginAt,
  DateTime Created )
{
  public UserResponse( AppUser u )
    : this( u.Id, u.Email, u.DisplayName, u.PictureUrl, u.IsAdmin, u.LastLoginAt, u.Created )
  {
  }
}
