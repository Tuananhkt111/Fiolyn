using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Requests.User ;
using Fiolyn.Controllers.DTOs.Responses.User ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Self-service profile endpoints and admin-only user management.
/// </summary>
[ApiController]
[Route( "api/users" )]
[Authorize]
public class UserController( IUserService userService ) : ControllerBase
{
  /// <summary>
  ///   Returns the profile of the currently authenticated user.
  /// </summary>
  [HttpGet( "me" )]
  public async Task<IActionResult> GetMeAsync()
  {
    var user = await userService.GetByIdAsync( User.GetUserId() ) ;
    return Ok( new UserResponse( user ) ) ;
  }

  /// <summary>
  ///   Updates the authenticated user's own profile (display name only).
  /// </summary>
  [HttpPatch( "me" )]
  public async Task<IActionResult> UpdateMeAsync( [FromBody] UpdateMeRequest request )
  {
    var user = await userService.UpdateDisplayNameAsync( User.GetUserId(), request.DisplayName ) ;
    return Ok( new UserResponse( user ) ) ;
  }

  /// <summary>
  ///   Links or updates the authenticated user's Telegram ID. 409 if the ID is taken by another user.
  /// </summary>
  [HttpPut( "me/telegram-id" )]
  public async Task<IActionResult> SetMyTelegramIdAsync( [FromBody] SetTelegramIdRequest request )
  {
    var user = await userService.UpdateTelegramIdAsync( User.GetUserId(), request.TelegramId ) ;
    return Ok( new UserResponse( user ) ) ;
  }

  /// <summary>
  ///   Unlinks the authenticated user's Telegram ID.
  /// </summary>
  [HttpDelete( "me/telegram-id" )]
  public async Task<IActionResult> ClearMyTelegramIdAsync()
  {
    var user = await userService.UpdateTelegramIdAsync( User.GetUserId(), null ) ;
    return Ok( new UserResponse( user ) ) ;
  }

  /// <summary>
  ///   Lists all active users. Admin only.
  /// </summary>
  [HttpGet]
  [Authorize( Policy = AuthConstants.AdminPolicyName )]
  public async Task<IActionResult> GetAllAsync()
  {
    var users = await userService.GetAllAsync() ;
    return Ok( users.Select( u => new UserResponse( u ) ).ToList() ) ;
  }

  /// <summary>
  ///   Toggles the admin flag on a user. Admin only.
  /// </summary>
  [HttpPatch( "{id:long}" )]
  [Authorize( Policy = AuthConstants.AdminPolicyName )]
  public async Task<IActionResult> SetAdminAsync( long id, [FromBody] UpdateRoleRequest request )
  {
    var user = await userService.SetAdminAsync( id, request.IsAdmin ) ;
    return Ok( new UserResponse( user ) ) ;
  }

  /// <summary>
  ///   Sets or updates a target user's Telegram ID. Admin only. 409 if the ID is taken by another user.
  /// </summary>
  [HttpPut( "{id:long}/telegram-id" )]
  [Authorize( Policy = AuthConstants.AdminPolicyName )]
  public async Task<IActionResult> SetTelegramIdAsync( long id, [FromBody] SetTelegramIdRequest request )
  {
    var user = await userService.UpdateTelegramIdAsync( id, request.TelegramId ) ;
    return Ok( new UserResponse( user ) ) ;
  }

  /// <summary>
  ///   Clears a target user's Telegram ID. Admin only.
  /// </summary>
  [HttpDelete( "{id:long}/telegram-id" )]
  [Authorize( Policy = AuthConstants.AdminPolicyName )]
  public async Task<IActionResult> ClearTelegramIdAsync( long id )
  {
    var user = await userService.UpdateTelegramIdAsync( id, null ) ;
    return Ok( new UserResponse( user ) ) ;
  }

  /// <summary>
  ///   Soft-deletes a user. Admin only.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  [Authorize( Policy = AuthConstants.AdminPolicyName )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await userService.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
