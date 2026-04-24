using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.User ;

/// <summary>
///   Request body for PUT /api/users/me/telegram-id and PUT /api/users/{id}/telegram-id.
/// </summary>
public record SetTelegramIdRequest( [Required] long TelegramId ) ;
