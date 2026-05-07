using Fiolyn.Controllers.DTOs.Requests.BorrowingTransaction ;
using Fiolyn.Controllers.DTOs.Responses.BorrowingTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing borrowing transactions on spend accounts the caller is a member of.
/// </summary>
[ApiController]
[Route( "api/borrowing-transactions" )]
[Authorize]
public class BorrowingTransactionController(
  IBorrowingTransactionService service ) : ControllerBase
{
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] BorrowingTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var entity = new BorrowingTransaction
    {
      SpendAccountId = request.SpendAccountId,
      CategoryId = request.CategoryId,
      Amount = request.Amount,
      Date = request.Date,
      Description = request.Description,
      PayerId = request.PayerId ?? userId,
      DueDate = request.DueDate
    } ;

    var created = await service.CreateAsync( entity, userId, request.CounterpartyName ) ;
    var refreshed = await service.GetByIdForUserAsync( created.Id, userId ) ;
    return CreatedAtRoute( "GetBorrowingTransactionById", new
    {
      id = created.Id
    }, new BorrowingTransactionResponse( refreshed! ) ) ;
  }

  [HttpGet]
  public async Task<IActionResult> GetAllAsync()
  {
    var list = await service.GetAllForUserAsync( User.GetUserId() ) ;
    return Ok( list.Select( t => new BorrowingTransactionResponse( t ) ).ToList() ) ;
  }

  [HttpGet( "{id:long}", Name = "GetBorrowingTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdForUserAsync( id, User.GetUserId() ) ;
    return entity is null ? NotFound() : Ok( new BorrowingTransactionResponse( entity ) ) ;
  }

  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var exists = await service.ExistsForUserAsync( id, User.GetUserId() ) ;
    return exists ? Ok() : NotFound() ;
  }

  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] BorrowingTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var existing = await service.GetByIdForUserAsync( id, userId ) ;
    if ( existing is null )
    {
      return NotFound() ;
    }

    existing.SpendAccountId = request.SpendAccountId ;
    existing.CategoryId = request.CategoryId ;
    existing.Amount = request.Amount ;
    existing.Date = request.Date ;
    existing.Description = request.Description ;
    existing.PayerId = request.PayerId ?? userId ;
    existing.DueDate = request.DueDate ;

    await service.UpdateAsync( existing, userId, request.CounterpartyName ) ;
    var refreshed = await service.GetByIdForUserAsync( id, userId ) ;
    return Ok( new BorrowingTransactionResponse( refreshed! ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }
}
