using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.User ;

/// <summary>
///   Request body for PATCH /api/users/me — self-service profile updates.
/// </summary>
public record UpdateMeRequest(
  [MaxLength( 256 )] string? DisplayName ) ;
