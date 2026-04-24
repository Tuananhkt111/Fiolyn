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
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] StockTransactionRequest request )
  {
    var userId = User.GetUserId() ;
    var transaction = new StockTransaction
    {
      Symbol = request.Symbol,
      TransactionType = request.TransactionType,
      Quantity = request.Quantity,
      Price = request.Price,
      TotalAmount = request.TotalAmount,
      TransactionDate = request.TransactionDate,
      BrokerAccountId = request.BrokerAccountId,
      Notes = request.Notes
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
  public async Task<IActionResult> GetAllAsync( [FromQuery] string? symbol = null )
  {
    var list = await stockTransactionService.GetAllForUserAsync( User.GetUserId(), symbol ) ;
    return Ok( list.Select( t => new StockTransactionResponse( t ) ).ToList() ) ;
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
  ///   reassigned broker (if any).
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

    existing.Symbol = request.Symbol ;
    existing.TransactionType = request.TransactionType ;
    existing.Quantity = request.Quantity ;
    existing.Price = request.Price ;
    existing.TotalAmount = request.TotalAmount ;
    existing.TransactionDate = request.TransactionDate ;
    existing.BrokerAccountId = request.BrokerAccountId ;
    existing.Notes = request.Notes ;

    await stockTransactionService.UpdateAsync( existing, userId ) ;
    return Ok( new StockTransactionResponse( existing ) ) ;
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
