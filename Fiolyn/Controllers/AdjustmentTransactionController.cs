using Fiolyn.Controllers.DTOs.Requests.AdjustmentTransaction ;
using Fiolyn.Controllers.DTOs.Responses.AdjustmentTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for manual signed adjustments on spend accounts the caller is a member of.
///   Replaces the legacy <c>POST /spend-accounts/{id}/balance-adjustments</c> endpoint with a
///   tracked, audited row.
/// </summary>
[ApiController]
[Route( "api/adjustment-transactions" )]
[Authorize]
public class AdjustmentTransactionController(
  IAdjustmentTransactionService service ) : ControllerBase
{
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] AdjustmentTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var entity = new AdjustmentTransaction
    {
      SpendAccountId = request.SpendAccountId,
      CategoryId = request.CategoryId,
      Amount = request.Delta,
      Date = request.Date,
      Description = request.Description,
      PayerId = request.PayerId ?? userId
    } ;

    var created = await service.CreateAsync( entity, userId ) ;
    var refreshed = await service.GetByIdForUserAsync( created.Id, userId ) ;
    return CreatedAtRoute( "GetAdjustmentTransactionById", new
    {
      id = created.Id
    }, new AdjustmentTransactionResponse( refreshed! ) ) ;
  }

  [HttpGet]
  public async Task<IActionResult> GetAllAsync()
  {
    var list = await service.GetAllForUserAsync( User.GetUserId() ) ;
    return Ok( list.Select( t => new AdjustmentTransactionResponse( t ) ).ToList() ) ;
  }

  [HttpGet( "{id:long}", Name = "GetAdjustmentTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdForUserAsync( id, User.GetUserId() ) ;
    return entity is null ? NotFound() : Ok( new AdjustmentTransactionResponse( entity ) ) ;
  }

  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var exists = await service.ExistsForUserAsync( id, User.GetUserId() ) ;
    return exists ? Ok() : NotFound() ;
  }

  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] AdjustmentTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var existing = await service.GetByIdForUserAsync( id, userId ) ;
    if ( existing is null )
    {
      return NotFound() ;
    }

    existing.SpendAccountId = request.SpendAccountId ;
    existing.CategoryId = request.CategoryId ;
    existing.Amount = request.Delta ;
    existing.Date = request.Date ;
    existing.Description = request.Description ;
    existing.PayerId = request.PayerId ?? userId ;

    await service.UpdateAsync( existing, userId ) ;
    var refreshed = await service.GetByIdForUserAsync( id, userId ) ;
    return Ok( new AdjustmentTransactionResponse( refreshed! ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }
}
