using Fiolyn.Common.Pagination ;
using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Requests.AllowedEmail ;
using Fiolyn.Controllers.DTOs.Responses.AllowedEmail ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Admin-only CRUD over the Google SSO email whitelist.
/// </summary>
[ApiController]
[Route( "api/allowed-emails" )]
[Authorize( Policy = AuthConstants.AdminPolicyName )]
public class AllowedEmailController( IAllowedEmailService allowedEmailService ) : ControllerBase
{
  /// <summary>
  ///   Lists all active whitelisted emails.
  /// </summary>
  [HttpGet( Name = "GetAllAsync" )]
  public async Task<IActionResult> GetAllAsync( [FromQuery] PaginationQuery pagination )
  {
    var page = await allowedEmailService.GetAllAsync( pagination ) ;
    return Ok( page.Map( a => new AllowedEmailResponse( a ) ) ) ;
  }

  /// <summary>
  ///   Adds an email to the whitelist.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] AllowedEmailRequest request )
  {
    var entry = await allowedEmailService.CreateAsync( request.Email, request.Note, User.TryGetUserId() ) ;
    return CreatedAtRoute( "GetAllAsync", new AllowedEmailResponse( entry ) ) ;
  }

  /// <summary>
  ///   Removes an email from the whitelist (soft delete).
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await allowedEmailService.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
