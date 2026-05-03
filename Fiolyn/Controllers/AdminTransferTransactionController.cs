using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Responses.TransferTransaction ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Admin-only REST API for managing transfer transactions across all users.
/// </summary>
[ApiController]
[Route( "api/admin/transfer-transactions" )]
[Authorize( Policy = AuthConstants.AdminPolicyName )]
public class AdminTransferTransactionController(
  ITransferTransactionService service ) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] long? userId = null )
  {
    var list = await service.GetAllAsync( userId ) ;
    return Ok( list.Select( t => new TransferTransactionResponse( t ) ).ToList() ) ;
  }

  [HttpGet( "{id:long}", Name = "GetAdminTransferTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdAsync( id ) ;
    return entity is null ? NotFound() : Ok( new TransferTransactionResponse( entity ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
