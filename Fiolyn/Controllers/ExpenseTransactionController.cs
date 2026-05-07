using Fiolyn.Controllers.DTOs.Requests.ExpenseTransaction ;
using Fiolyn.Controllers.DTOs.Requests.Shared ;
using Fiolyn.Controllers.DTOs.Responses.ExpenseTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing expense transactions on spend accounts the caller is a member of.
/// </summary>
[ApiController]
[Route( "api/expense-transactions" )]
[Authorize]
public class ExpenseTransactionController(
  IExpenseTransactionService service ) : ControllerBase
{
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] ExpenseTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var entity = new ExpenseTransaction
    {
      SpendAccountId = request.SpendAccountId,
      CategoryId = request.CategoryId,
      Amount = -request.Amount,
      Date = request.Date,
      Description = request.Description,
      PayerId = request.PayerId ?? userId
    } ;

    var created = await service.CreateAsync( entity, userId, request.Shares.ToShareInputs() ) ;
    var refreshed = await service.GetByIdForUserAsync( created.Id, userId ) ;
    return CreatedAtRoute( "GetExpenseTransactionById", new
    {
      id = created.Id
    }, new ExpenseTransactionResponse( refreshed! ) ) ;
  }

  [HttpGet]
  public async Task<IActionResult> GetAllAsync()
  {
    var list = await service.GetAllForUserAsync( User.GetUserId() ) ;
    return Ok( list.Select( t => new ExpenseTransactionResponse( t ) ).ToList() ) ;
  }

  [HttpGet( "{id:long}", Name = "GetExpenseTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdForUserAsync( id, User.GetUserId() ) ;
    return entity is null ? NotFound() : Ok( new ExpenseTransactionResponse( entity ) ) ;
  }

  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var exists = await service.ExistsForUserAsync( id, User.GetUserId() ) ;
    return exists ? Ok() : NotFound() ;
  }

  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] ExpenseTransactionRequest request )
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

    await service.UpdateAsync( existing, userId, request.Shares.ToShareInputs() ) ;
    var refreshed = await service.GetByIdForUserAsync( id, userId ) ;
    return Ok( new ExpenseTransactionResponse( refreshed! ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }
}
