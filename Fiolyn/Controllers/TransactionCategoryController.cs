using Fiolyn.Controllers.DTOs.Responses.TransactionCategory ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

[ApiController]
[Route( "api/transaction-categories" )]
[Authorize]
public class TransactionCategoryController(
  ITransactionCategoryService service ) : ControllerBase
{
  /// <summary>
  ///   Lists active categories, optionally filtered by kind.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] TransactionKind? kind = null )
  {
    var list = await service.GetAllAsync( kind ) ;
    return Ok( list.Select( c => new TransactionCategoryResponse( c ) ) ) ;
  }
}