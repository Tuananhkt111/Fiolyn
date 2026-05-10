using Fiolyn.Common.Pagination ;
using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Responses.StockTransaction ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Admin-only REST API for managing stock transactions across all users.
/// </summary>
[ApiController]
[Route( "api/admin/stock-transactions" )]
[Authorize( Policy = AuthConstants.AdminPolicyName )]
public class AdminStockTransactionController(
  IStockTransactionService stockTransactionService ) : ControllerBase
{
  /// <summary>
  ///   Lists stock transactions across all users. Optionally filter by broker membership user and symbol.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync(
    [FromQuery] PaginationQuery pagination,
    [FromQuery] long? userId = null,
    [FromQuery] string? symbol = null )
  {
    var page = await stockTransactionService.GetAllAsync( pagination, userId, symbol ) ;
    return Ok( page.Map( t => new StockTransactionResponse( t ) ) ) ;
  }

  /// <summary>
  ///   Gets any stock transaction by ID (not scoped to caller).
  /// </summary>
  [HttpGet( "{id:long}", Name = "GetAdminStockTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var transaction = await stockTransactionService.GetByIdAsync( id ) ;
    if ( transaction is null )
    {
      return NotFound() ;
    }

    return Ok( new StockTransactionResponse( transaction ) ) ;
  }

  /// <summary>
  ///   Soft-deletes any stock transaction.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await stockTransactionService.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
