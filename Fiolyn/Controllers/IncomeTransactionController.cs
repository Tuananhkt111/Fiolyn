using Fiolyn.Common.Pagination ;
using Fiolyn.Controllers.DTOs.Requests.IncomeTransaction ;
using Fiolyn.Controllers.DTOs.Requests.Shared ;
using Fiolyn.Controllers.DTOs.Responses.IncomeTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing income transactions on spend accounts the caller is a member of.
/// </summary>
[ApiController]
[Route( "api/income-transactions" )]
[Authorize]
public class IncomeTransactionController(
  IIncomeTransactionService service ) : ControllerBase
{
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] IncomeTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var entity = new IncomeTransaction
    {
      SpendAccountId = request.SpendAccountId,
      CategoryId = request.CategoryId,
      Amount = request.Amount,
      Date = request.Date,
      Description = request.Description,
      PayerId = request.PayerId ?? userId
    } ;

    var created = await service.CreateAsync( entity, userId, request.Shares.ToShareInputs() ) ;
    var refreshed = await service.GetByIdForUserAsync( created.Id, userId ) ;
    return CreatedAtRoute( "GetIncomeTransactionById", new
    {
      id = created.Id
    }, new IncomeTransactionResponse( refreshed! ) ) ;
  }

  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] PaginationQuery pagination )
  {
    var page = await service.GetAllForUserAsync( User.GetUserId(), pagination ) ;
    return Ok( page.Map( t => new IncomeTransactionResponse( t ) ) ) ;
  }

  [HttpGet( "{id:long}", Name = "GetIncomeTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdForUserAsync( id, User.GetUserId() ) ;
    return entity is null ? NotFound() : Ok( new IncomeTransactionResponse( entity ) ) ;
  }

  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var exists = await service.ExistsForUserAsync( id, User.GetUserId() ) ;
    return exists ? Ok() : NotFound() ;
  }

  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] IncomeTransactionRequest request )
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

    await service.UpdateAsync( existing, userId, request.Shares.ToShareInputs() ) ;
    var refreshed = await service.GetByIdForUserAsync( id, userId ) ;
    return Ok( new IncomeTransactionResponse( refreshed! ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }
}
