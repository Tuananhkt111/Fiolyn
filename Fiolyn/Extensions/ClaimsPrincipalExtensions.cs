using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;

namespace Fiolyn.Extensions ;

/// <summary>
///   Helpers for reading identity claims from the current <see cref="ClaimsPrincipal" />.
/// </summary>
public static class ClaimsPrincipalExtensions
{
  /// <summary>
  ///   Reads the authenticated user's ID from the JWT `sub` claim.
  ///   Returns null if the claim is absent or malformed.
  /// </summary>
  public static long? TryGetUserId( this ClaimsPrincipal principal )
  {
    var sub = principal.FindFirst( JwtRegisteredClaimNames.Sub )?.Value
              ?? principal.FindFirst( ClaimTypes.NameIdentifier )?.Value ;
    return long.TryParse( sub, out var id ) ? id : null ;
  }

  /// <summary>
  ///   Reads the authenticated user's ID. Throws <see cref="UnauthorizedAccessException" />
  ///   if the claim is absent or malformed (should never happen inside an [Authorize]d action).
  /// </summary>
  public static long GetUserId( this ClaimsPrincipal principal ) => principal.TryGetUserId() ?? throw new UnauthorizedAccessException() ;
}