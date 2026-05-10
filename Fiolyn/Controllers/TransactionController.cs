using Fiolyn.Common.Pagination ;
using Fiolyn.Controllers.DTOs.Responses.Transaction ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Unified read-only API across every transaction kind. Shows a single chronological history
///   regardless of leaf type. Membership predicate unions <see cref="Entities.SpendAccount" />
///   membership and (for stock rows) <see cref="Entities.BrokerAccount" /> membership.
/// </summary>
[ApiController]
[Route( "api/transactions" )]
[Authorize]
public class TransactionController(
  ITransactionReadService readService ) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] PaginationQuery pagination )
  {
    var page = await readService.GetAllForUserAsync( User.GetUserId(), pagination ) ;
    return Ok( page.Map( TransactionResponse.FromEntity ) ) ;
  }
}
