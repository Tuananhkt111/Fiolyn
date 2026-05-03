using Fiolyn.Controllers.DTOs.Requests.LendTransaction ;
using Fiolyn.Controllers.DTOs.Responses.LendTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing lend transactions on spend accounts the caller is a member of.
/// </summary>
[ApiController]
[Route( "api/lend-transactions" )]
[Authorize]
public class LendTransactionController(
  ILendTransactionService service ) : ControllerBase
{
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] LendTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var entity = new LendTransaction
    {
      SpendAccountId = request.SpendAccountId,
      CategoryId = request.CategoryId,
      Amount = -request.Amount,
      Date = request.Date,
      Description = request.Description,
      PayerId = request.PayerId ?? userId,
      CounterpartyName = request.CounterpartyName,
      DueDate = request.DueDate
    } ;

    var created = await service.CreateAsync( entity, userId ) ;
    var refreshed = await service.GetByIdForUserAsync( created.Id, userId ) ;
    return CreatedAtRoute( "GetLendTransactionById", new
    {
      id = created.Id
    }, new LendTransactionResponse( refreshed! ) ) ;
  }

  [HttpGet]
  public async Task<IActionResult> GetAllAsync()
  {
    var list = await service.GetAllForUserAsync( User.GetUserId() ) ;
    return Ok( list.Select( t => new LendTransactionResponse( t ) ).ToList() ) ;
  }

  [HttpGet( "{id:long}", Name = "GetLendTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdForUserAsync( id, User.GetUserId() ) ;
    return entity is null ? NotFound() : Ok( new LendTransactionResponse( entity ) ) ;
  }

  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var exists = await service.ExistsForUserAsync( id, User.GetUserId() ) ;
    return exists ? Ok() : NotFound() ;
  }

  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] LendTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var existing = await service.GetByIdForUserAsync( id, userId ) ;
    if ( existing is null )
    {
      return NotFound() ;
    }

    existing.SpendAccountId = request.SpendAccountId ;
    existing.CategoryId = request.CategoryId ;
    existing.Amount = -request.Amount ;
    existing.Date = request.Date ;
    existing.Description = request.Description ;
    existing.PayerId = request.PayerId ?? userId ;
    existing.CounterpartyName = request.CounterpartyName ;
    existing.DueDate = request.DueDate ;

    await service.UpdateAsync( existing, userId ) ;
    var refreshed = await service.GetByIdForUserAsync( id, userId ) ;
    return Ok( new LendTransactionResponse( refreshed! ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }
}
