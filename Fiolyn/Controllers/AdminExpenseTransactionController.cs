using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Responses.ExpenseTransaction ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Admin-only REST API for managing expense transactions across all users.
/// </summary>
[ApiController]
[Route( "api/admin/expense-transactions" )]
[Authorize( Policy = AuthConstants.AdminPolicyName )]
public class AdminExpenseTransactionController(
  IExpenseTransactionService service ) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] long? userId = null )
  {
    var list = await service.GetAllAsync( userId ) ;
    return Ok( list.Select( t => new ExpenseTransactionResponse( t ) ).ToList() ) ;
  }

  [HttpGet( "{id:long}", Name = "GetAdminExpenseTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdAsync( id ) ;
    return entity is null ? NotFound() : Ok( new ExpenseTransactionResponse( entity ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
