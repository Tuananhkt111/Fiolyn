using Fiolyn.Common.Pagination ;
using Fiolyn.Controllers.DTOs.Requests.StockTransaction ;
using Fiolyn.Controllers.DTOs.Responses.StockTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing stock transactions on broker accounts the caller is a member of.
/// </summary>
[ApiController]
[Route( "api/stock-transactions" )]
[Authorize]
public class StockTransactionController(
  IStockTransactionService stockTransactionService ) : ControllerBase
{
  /// <summary>
  ///   Creates a new stock transaction. 404 if the caller is not a member of the target broker account.
  ///   For a Dividend with a positive <c>Quantity</c> and <c>Price</c> the total is computed as
  ///   quantity × price (per-share dividend) and the supplied <c>TotalAmount</c> is ignored.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] StockTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var transaction = new StockTransaction
    {
      Symbol = request.Symbol,
      StockTransactionType = request.StockTransactionType,
      Quantity = request.Quantity,
      Price = request.Price,
      Amount = StockTransaction.ComputeSignedAmount(
        request.StockTransactionType, request.Quantity, request.Price, request.TotalAmount ),
      Date = request.Date,
      BrokerAccountId = request.BrokerAccountId,
      Description = request.Description,
      CategoryId = request.CategoryId,
      PayerId = request.PayerId ?? userId
    } ;

    var created = await stockTransactionService.CreateAsync( transaction, userId ) ;
    var withNavigation = await stockTransactionService.GetByIdForUserAsync( created.Id, userId ) ;
    return CreatedAtRoute( "GetStockTransactionById", new
    {
      id = created.Id
    }, new StockTransactionResponse( withNavigation! ) ) ;
  }

  /// <summary>
  ///   Lists stock transactions on brokers the caller is a member of, optionally filtered by symbol.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync(
    [FromQuery] PaginationQuery pagination, [FromQuery] string? symbol = null )
  {
    var page = await stockTransactionService.GetAllForUserAsync( User.GetUserId(), pagination, symbol ) ;
    return Ok( page.Map( t => new StockTransactionResponse( t ) ) ) ;
  }

  /// <summary>
  ///   Gets a stock transaction by ID. 404 if the caller is not a member of its broker.
  /// </summary>
  [HttpGet( "{id:long}", Name = "GetStockTransactionById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var transaction = await stockTransactionService.GetByIdForUserAsync( id, User.GetUserId() ) ;
    if ( transaction is null )
    {
      return NotFound() ;
    }

    return Ok( new StockTransactionResponse( transaction ) ) ;
  }

  /// <summary>
  ///   Checks whether an active stock transaction exists and caller is a member of its broker.
  /// </summary>
  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var exists = await stockTransactionService.ExistsForUserAsync( id, User.GetUserId() ) ;
    return exists ? Ok() : NotFound() ;
  }

  /// <summary>
  ///   Updates a stock transaction. 404 if the caller is not a member of its current broker or the
  ///   reassigned broker (if any). For a Dividend with a positive <c>Quantity</c> and <c>Price</c>
  ///   the total is computed as quantity × price (per-share dividend) and the supplied
  ///   <c>TotalAmount</c> is ignored.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] StockTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var existing = await stockTransactionService.GetByIdForUserAsync( id, userId ) ;
    if ( existing is null )
    {
      return NotFound() ;
    }

    var oldBrokerId = existing.BrokerAccountId ;
    existing.Symbol = request.Symbol ;
    existing.StockTransactionType = request.StockTransactionType ;
    existing.Quantity = request.Quantity ;
    existing.Price = request.Price ;
    existing.Amount = StockTransaction.ComputeSignedAmount(
      request.StockTransactionType, request.Quantity, request.Price, request.TotalAmount ) ;
    existing.Date = request.Date ;
    existing.BrokerAccountId = request.BrokerAccountId ;
    existing.Description = request.Description ;
    existing.CategoryId = request.CategoryId ;
    existing.PayerId = request.PayerId ?? userId ;

    await stockTransactionService.UpdateAsync( existing, userId ) ;

    if ( oldBrokerId == request.BrokerAccountId )
    {
      return Ok( new StockTransactionResponse( existing ) ) ;
    }

    var refreshed = await stockTransactionService.GetByIdForUserAsync( id, userId ) ;
    return Ok( new StockTransactionResponse( refreshed! ) ) ;
  }

  /// <summary>
  ///   Soft-deletes a stock transaction. 404 if the caller is not a member of its broker.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await stockTransactionService.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }
}
