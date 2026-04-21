using Fiolyn.Controllers.DTOs.Requests.Auth ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Authentication endpoints — Google SSO login, refresh rotation, and logout.
/// </summary>
[ApiController]
[Route( "api/auth" )]
public class AuthController( IAuthService authService ) : ControllerBase
{
  /// <summary>
  ///   Exchanges a Google ID token for Fiolyn access + refresh tokens.
  /// </summary>
  [HttpPost( "google" )]
  [AllowAnonymous]
  public async Task<IActionResult> LoginWithGoogleAsync( [FromBody] GoogleLoginRequest request )
  {
    var result = await authService.LoginWithGoogleAsync( request.IdToken ) ;
    return Ok( result ) ;
  }

  /// <summary>
  ///   Rotates a refresh token. Returns a new access + refresh token pair.
  /// </summary>
  [HttpPost( "refresh" )]
  [AllowAnonymous]
  public async Task<IActionResult> RefreshAsync( [FromBody] RefreshTokenRequest request )
  {
    var result = await authService.RefreshAsync( request.RefreshToken ) ;
    return Ok( result ) ;
  }

  /// <summary>
  ///   Revokes the given refresh token.
  /// </summary>
  [HttpPost( "logout" )]
  [Authorize]
  public async Task<IActionResult> LogoutAsync( [FromBody] RefreshTokenRequest request )
  {
    await authService.LogoutAsync( request.RefreshToken ) ;
    return NoContent() ;
  }
}
