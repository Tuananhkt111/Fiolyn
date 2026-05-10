using Fiolyn.Common.Pagination ;
using Fiolyn.Controllers.DTOs.Requests.Counterparty ;
using Fiolyn.Controllers.DTOs.Responses.Counterparty ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing the caller's private counterparty contact list.
/// </summary>
[ApiController]
[Route( "api/counterparties" )]
[Authorize]
public class CounterpartyController(
  ICounterpartyService service ) : ControllerBase
{
  /// <summary>
  ///   Lists counterparties owned by the caller, ordered by name.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] PaginationQuery pagination )
  {
    var page = await service.GetAllForUserAsync( User.GetUserId(), pagination ) ;
    return Ok( page.Map( c => new CounterpartyResponse( c ) ) ) ;
  }

  /// <summary>
  ///   Gets a single counterparty by id. 404 if not owned by the caller.
  /// </summary>
  [HttpGet( "{id:long}", Name = "GetCounterpartyById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entry = await service.GetByIdForUserAsync( id, User.GetUserId() ) ;
    return entry is null ? NotFound() : Ok( new CounterpartyResponse( entry ) ) ;
  }

  /// <summary>
  ///   Cheap existence check used by HEAD callers.
  /// </summary>
  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var entry = await service.GetByIdForUserAsync( id, User.GetUserId() ) ;
    return entry is null ? NotFound() : Ok() ;
  }

  /// <summary>
  ///   Creates a counterparty for the caller. 409 on duplicate (case-insensitive).
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] CounterpartyRequest request )
  {
    var entry = await service.CreateAsync( request.Name, User.GetUserId() ) ;
    return CreatedAtRoute( "GetCounterpartyById", new
    {
      id = entry.Id
    }, new CounterpartyResponse( entry ) ) ;
  }

  /// <summary>
  ///   Renames a counterparty. 404 if not owned; 409 on collision.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] CounterpartyRequest request )
  {
    var updated = await service.UpdateAsync( id, User.GetUserId(), request.Name ) ;
    return updated is null ? NotFound() : Ok( new CounterpartyResponse( updated ) ) ;
  }

  /// <summary>
  ///   Soft-deletes a counterparty. 400 if any active lend/borrow row still references it.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }
}
