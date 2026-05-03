using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Requests.TransactionCategory ;
using Fiolyn.Controllers.DTOs.Responses.TransactionCategory ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Admin-only CRUD over the transaction-category lookup. Each row is bound to one
///   <see cref="TransactionKind" /> at create time.
/// </summary>
[ApiController]
[Route( "api/admin/transaction-categories" )]
[Authorize( Policy = AuthConstants.AdminPolicyName )]
public class AdminTransactionCategoryController(
  ITransactionCategoryService service ) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] TransactionKind? kind = null )
  {
    var list = await service.GetAllAsync( kind ) ;
    return Ok( list.Select( c => new TransactionCategoryResponse( c ) ).ToList() ) ;
  }

  [HttpGet( "{id:long}", Name = "GetTransactionCategoryById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var entity = await service.GetByIdAsync( id ) ;
    return entity is null ? NotFound() : Ok( new TransactionCategoryResponse( entity ) ) ;
  }

  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] TransactionCategoryCreateRequest request )
  {
    var entry = await service.CreateAsync( request.Name, request.Kind ) ;
    return CreatedAtRoute( "GetTransactionCategoryById", new
    {
      id = entry.Id
    }, new TransactionCategoryResponse( entry ) ) ;
  }

  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] TransactionCategoryUpdateRequest request )
  {
    var entry = await service.UpdateAsync( id, request.Name ) ;
    return Ok( new TransactionCategoryResponse( entry ) ) ;
  }

  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await service.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
