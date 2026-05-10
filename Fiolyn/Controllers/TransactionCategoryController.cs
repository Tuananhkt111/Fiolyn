using Fiolyn.Common.Pagination ;
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
  public async Task<IActionResult> GetAllAsync(
    [FromQuery] PaginationQuery pagination, [FromQuery] TransactionKind? kind = null )
  {
    var page = await service.GetAllAsync( pagination, kind ) ;
    return Ok( page.Map( c => new TransactionCategoryResponse( c ) ) ) ;
  }
}
