using Fiolyn.Controllers.DTOs.Requests.StockTransaction ;
using Fiolyn.Controllers.DTOs.Responses.StockTransaction ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing stock transactions.
/// </summary>
[ApiController]
[Route( "api/stock-transactions" )]
[Authorize]
public class StockTransactionController(
  IStockTransactionService stockTransactionService ) : ControllerBase
{
  /// <summary>
  ///   Creates a new stock transaction.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] StockTransactionRequest request )
  {
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

    var created = await stockTransactionService.CreateAsync( transaction ) ;
    var withNavigation = await stockTransactionService.GetByIdAsync( created.Id ) ;
    return CreatedAtRoute( "GetStockTransactionById", new
    {
      id = created.Id
    }, new StockTransactionResponse( withNavigation! ) ) ;
  }

  /// <summary>
  ///   Lists all stock transactions, optionally filtered by symbol.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] string? symbol = null )
  {
    var list = await stockTransactionService.GetAllAsync( symbol ) ;
    return Ok( list.Select( t => new StockTransactionResponse( t ) ).ToList() ) ;
  }

  /// <summary>
  ///   Gets a stock transaction by ID.
  /// </summary>
  [HttpGet( "{id:long}", Name = "GetStockTransactionById" )]
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
  ///   Checks whether an active stock transaction exists.
  /// </summary>
  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var exists = await stockTransactionService.ExistsAsync( id ) ;
    return exists ? Ok() : NotFound() ;
  }

  /// <summary>
  ///   Updates a stock transaction.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] StockTransactionRequest request )
  {
    var existing = await stockTransactionService.GetByIdAsync( id ) ;
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

    await stockTransactionService.UpdateAsync( existing ) ;
    return Ok( new StockTransactionResponse( existing ) ) ;
  }

  /// <summary>
  ///   Soft-deletes a stock transaction.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await stockTransactionService.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
