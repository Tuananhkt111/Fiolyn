using Fiolyn.Controllers.DTOs.Requests.StockTransaction ;
using Fiolyn.Controllers.DTOs.Responses ;
using Fiolyn.Entities ;
using Fiolyn.Services.External.Stock ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing stock transactions.
/// </summary>
[ApiController]
[Route( "api/stock-transactions" )]
public class StockTransactionController(
  IStockTransactionService stockTransactionService,
  ILogger<StockTransactionController> logger ) : ControllerBase
{
  /// <summary>
  ///   Creates a new stock transaction.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] StockTransactionRequest request )
  {
    try
    {
      var transaction = new StockTransaction
      {
        Symbol = request.Symbol.ToUpperInvariant(),
        TransactionType = request.TransactionType,
        Quantity = request.Quantity,
        Price = request.Price,
        TotalAmount = request.TotalAmount,
        TransactionDate = request.TransactionDate,
        BrokerAccountId = request.BrokerAccountId,
        Notes = request.Notes
      } ;

      var created = await stockTransactionService.CreateAsync( transaction ) ;
      return Ok( new ApiResponse<StockTransaction>( true, created ) ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Failed to create stock transaction" ) ;
      return Ok( new ApiResponse<StockTransaction>( false, Error: ex.Message ) ) ;
    }
  }

  /// <summary>
  ///   Lists all stock transactions, optionally filtered by symbol.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] string? symbol = null )
  {
    try
    {
      var list = await stockTransactionService.GetAllAsync( symbol ) ;
      return Ok( new ApiResponse<List<StockTransaction>>( true, list ) ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Failed to list stock transactions" ) ;
      return Ok( new ApiResponse<List<StockTransaction>>( false, Error: ex.Message ) ) ;
    }
  }

  /// <summary>
  ///   Gets a stock transaction by ID.
  /// </summary>
  [HttpGet( "{id:long}" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    try
    {
      var transaction = await stockTransactionService.GetByIdAsync( id ) ;
      if ( transaction is null )
      {
        return Ok( new ApiResponse<StockTransaction>( false, Error: "Not found" ) ) ;
      }

      return Ok( new ApiResponse<StockTransaction>( true, transaction ) ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Failed to get stock transaction {Id}", id ) ;
      return Ok( new ApiResponse<StockTransaction>( false, Error: ex.Message ) ) ;
    }
  }

  /// <summary>
  ///   Updates a stock transaction.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] StockTransactionRequest request )
  {
    try
    {
      var existing = await stockTransactionService.GetByIdAsync( id ) ;
      if ( existing is null )
      {
        return Ok( new ApiResponse<StockTransaction>( false, Error: "Not found" ) ) ;
      }

      existing.Symbol = request.Symbol.ToUpperInvariant() ;
      existing.TransactionType = request.TransactionType ;
      existing.Quantity = request.Quantity ;
      existing.Price = request.Price ;
      existing.TotalAmount = request.TotalAmount ;
      existing.TransactionDate = request.TransactionDate ;
      existing.BrokerAccountId = request.BrokerAccountId ;
      existing.Notes = request.Notes ;

      await stockTransactionService.UpdateAsync( existing ) ;
      return Ok( new ApiResponse<StockTransaction>( true, existing ) ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Failed to update stock transaction {Id}", id ) ;
      return Ok( new ApiResponse<StockTransaction>( false, Error: ex.Message ) ) ;
    }
  }

  /// <summary>
  ///   Soft-deletes a stock transaction.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    try
    {
      await stockTransactionService.SoftDeleteAsync( id ) ;
      return Ok( new ApiResponse<object>( true ) ) ;
    }
    catch ( KeyNotFoundException )
    {
      return Ok( new ApiResponse<object>( false, Error: "Not found" ) ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Failed to delete stock transaction {Id}", id ) ;
      return Ok( new ApiResponse<object>( false, Error: ex.Message ) ) ;
    }
  }
}