namespace Fiolyn.Controllers.DTOs.Requests.User ;

/// <summary>
///   Request body for admin PATCH /api/users/{id} — toggles the admin flag.
/// </summary>
public record UpdateRoleRequest( bool IsAdmin ) ;