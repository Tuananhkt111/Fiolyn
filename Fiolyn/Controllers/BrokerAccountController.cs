using Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;
using Fiolyn.Controllers.DTOs.Responses ;
using Fiolyn.Entities ;
using Fiolyn.Services.External.Stock ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing broker accounts.
/// </summary>
[ApiController]
[Route( "api/broker-accounts" )]
public class BrokerAccountController(
  IBrokerAccountService brokerAccountService,
  ILogger<BrokerAccountController> logger ) : ControllerBase
{
  /// <summary>
  ///   Creates a new broker account.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] BrokerAccountRequest request )
  {
    try
    {
      var brokerAccount = new BrokerAccount
      {
        Name = request.Name
      } ;

      var created = await brokerAccountService.CreateAsync( brokerAccount ) ;
      return Ok( new ApiResponse<BrokerAccount>( true, created ) ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Failed to create broker account" ) ;
      return Ok( new ApiResponse<BrokerAccount>( false, Error: ex.Message ) ) ;
    }
  }

  /// <summary>
  ///   Lists all broker accounts.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync()
  {
    try
    {
      var list = await brokerAccountService.GetAllAsync() ;
      return Ok( new ApiResponse<List<BrokerAccount>>( true, list ) ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Failed to list broker accounts" ) ;
      return Ok( new ApiResponse<List<BrokerAccount>>( false, Error: ex.Message ) ) ;
    }
  }

  /// <summary>
  ///   Gets a broker account by ID.
  /// </summary>
  [HttpGet( "{id:long}" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    try
    {
      var brokerAccount = await brokerAccountService.GetByIdAsync( id ) ;
      if ( brokerAccount is null )
      {
        return Ok( new ApiResponse<BrokerAccount>( false, Error: "Not found" ) ) ;
      }

      return Ok( new ApiResponse<BrokerAccount>( true, brokerAccount ) ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Failed to get broker account {Id}", id ) ;
      return Ok( new ApiResponse<BrokerAccount>( false, Error: ex.Message ) ) ;
    }
  }

  /// <summary>
  ///   Updates a broker account.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] BrokerAccountRequest request )
  {
    try
    {
      var existing = await brokerAccountService.GetByIdAsync( id ) ;
      if ( existing is null )
      {
        return Ok( new ApiResponse<BrokerAccount>( false, Error: "Not found" ) ) ;
      }

      existing.Name = request.Name ;

      await brokerAccountService.UpdateAsync( existing ) ;
      return Ok( new ApiResponse<BrokerAccount>( true, existing ) ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Failed to update broker account {Id}", id ) ;
      return Ok( new ApiResponse<BrokerAccount>( false, Error: ex.Message ) ) ;
    }
  }

  /// <summary>
  ///   Soft-deletes a broker account.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    try
    {
      await brokerAccountService.SoftDeleteAsync( id ) ;
      return Ok( new ApiResponse<object>( true ) ) ;
    }
    catch ( KeyNotFoundException )
    {
      return Ok( new ApiResponse<object>( false, Error: "Not found" ) ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Failed to delete broker account {Id}", id ) ;
      return Ok( new ApiResponse<object>( false, Error: ex.Message ) ) ;
    }
  }
}