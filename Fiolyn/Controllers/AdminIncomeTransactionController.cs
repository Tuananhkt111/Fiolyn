using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Responses.IncomeTransaction ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Admin-only REST API for managing income transactions across all users.
/// </summary>
[ApiController]
[Route( "api/admin/income-transactions" )]
[Authorize( Policy = AuthConstants.AdminPolicyName )]
public class AdminIncomeTransactionController(
  IIncomeTransactionService service ) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] long? userId = null )
  {
    var list = await service.GetAllAsync( userId ) ;
    return Ok( list.Select( t => new IncomeTransactionResponse( t ) ).ToList() ) ;
  }

  [HttpGet( "{id:long}", Name = "GetAdminIncomeTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdAsync( id ) ;
    return entity is null ? NotFound() : Ok( new IncomeTransactionResponse( entity ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
