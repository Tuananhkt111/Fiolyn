using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Responses.Transaction ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Admin-only unified read across every transaction kind, with optional user filter.
/// </summary>
[ApiController]
[Route( "api/admin/transactions" )]
[Authorize( Policy = AuthConstants.AdminPolicyName )]
public class AdminTransactionController(
  ITransactionReadService readService ) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] long? userId = null )
  {
    var list = await readService.GetAllAsync( userId ) ;
    return Ok( list.Select( TransactionResponse.FromEntity ).ToList() ) ;
  }
}
