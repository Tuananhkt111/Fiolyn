using Fiolyn.Common.Pagination ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API exposing the obligations view: per-counterparty receivable / payable / net summing
///   the caller's lend + borrow rows (auto-paired share children included), scoped to the
///   calling user since counterparties are per-user.
/// </summary>
[ApiController]
[Route( "api/obligations" )]
[Authorize]
public class ObligationController(
  IObligationService service ) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] PaginationQuery pagination )
  {
    var page = await service.GetAllForUserAsync( User.GetUserId(), pagination ) ;
    return Ok( page ) ;
  }
}
