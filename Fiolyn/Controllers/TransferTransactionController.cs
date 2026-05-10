using Fiolyn.Common.Pagination ;
using Fiolyn.Controllers.DTOs.Requests.TransferTransaction ;
using Fiolyn.Controllers.DTOs.Responses.TransferTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing transfers between two spend accounts the caller is a member of.
/// </summary>
[ApiController]
[Route( "api/transfer-transactions" )]
[Authorize]
public class TransferTransactionController(
  ITransferTransactionService service ) : ControllerBase
{
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] TransferTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var entity = new TransferTransaction
    {
      SpendAccountId = request.SpendAccountId,
      DestinationSpendAccountId = request.DestinationSpendAccountId,
      CategoryId = request.CategoryId,
      Amount = request.Amount,
      Date = request.Date,
      Description = request.Description,
      PayerId = request.PayerId ?? userId
    } ;

    var created = await service.CreateAsync( entity, userId ) ;
    var refreshed = await service.GetByIdForUserAsync( created.Id, userId ) ;
    return CreatedAtRoute( "GetTransferTransactionById", new
    {
      id = created.Id
    }, new TransferTransactionResponse( refreshed! ) ) ;
  }

  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] PaginationQuery pagination )
  {
    var page = await service.GetAllForUserAsync( User.GetUserId(), pagination ) ;
    return Ok( page.Map( t => new TransferTransactionResponse( t ) ) ) ;
  }

  [HttpGet( "{id:long}", Name = "GetTransferTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdForUserAsync( id, User.GetUserId() ) ;
    return entity is null ? NotFound() : Ok( new TransferTransactionResponse( entity ) ) ;
  }

  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var exists = await service.ExistsForUserAsync( id, User.GetUserId() ) ;
    return exists ? Ok() : NotFound() ;
  }

  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] TransferTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var existing = await service.GetByIdForUserAsync( id, userId ) ;
    if ( existing is null )
    {
      return NotFound() ;
    }

    existing.SpendAccountId = request.SpendAccountId ;
    existing.DestinationSpendAccountId = request.DestinationSpendAccountId ;
    existing.CategoryId = request.CategoryId ;
    existing.Amount = request.Amount ;
    existing.Date = request.Date ;
    existing.Description = request.Description ;
    existing.PayerId = request.PayerId ?? userId ;

    await service.UpdateAsync( existing, userId ) ;
    var refreshed = await service.GetByIdForUserAsync( id, userId ) ;
    return Ok( new TransferTransactionResponse( refreshed! ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }
}
