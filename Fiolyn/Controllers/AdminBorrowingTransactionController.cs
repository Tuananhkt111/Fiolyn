using Fiolyn.Common.Pagination ;
using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Responses.BorrowingTransaction ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Admin-only REST API for managing borrowing transactions across all users.
/// </summary>
[ApiController]
[Route( "api/admin/borrowing-transactions" )]
[Authorize( Policy = AuthConstants.AdminPolicyName )]
public class AdminBorrowingTransactionController(
  IBorrowingTransactionService service ) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetAllAsync(
    [FromQuery] PaginationQuery pagination, [FromQuery] long? userId = null )
  {
    var page = await service.GetAllAsync( pagination, userId ) ;
    return Ok( page.Map( t => new BorrowingTransactionResponse( t ) ) ) ;
  }

  [HttpGet( "{id:long}", Name = "GetAdminBorrowingTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdAsync( id ) ;
    return entity is null ? NotFound() : Ok( new BorrowingTransactionResponse( entity ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
