using Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;
using Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing broker accounts.
/// </summary>
[ApiController]
[Route( "api/broker-accounts" )]
[Authorize]
public class BrokerAccountController(
  IBrokerAccountService brokerAccountService ) : ControllerBase
{
  /// <summary>
  ///   Creates a new broker account.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] BrokerAccountRequest request )
  {
    var brokerAccount = new BrokerAccount
    {
      Name = request.Name
    } ;

    var created = await brokerAccountService.CreateAsync( brokerAccount ) ;
    return CreatedAtRoute( "GetBrokerAccountById", new
    {
      id = created.Id
    }, new BrokerAccountResponse( created ) ) ;
  }

  /// <summary>
  ///   Lists all broker accounts.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync()
  {
    var list = await brokerAccountService.GetAllAsync() ;
    return Ok( list.Select( b => new BrokerAccountResponse( b ) ).ToList() ) ;
  }

  /// <summary>
  ///   Gets a broker account by ID.
  /// </summary>
  [HttpGet( "{id:long}", Name = "GetBrokerAccountById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var brokerAccount = await brokerAccountService.GetByIdAsync( id ) ;
    if ( brokerAccount is null )
    {
      return NotFound() ;
    }

    return Ok( new BrokerAccountResponse( brokerAccount ) ) ;
  }

  /// <summary>
  ///   Checks whether an active broker account exists.
  /// </summary>
  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var exists = await brokerAccountService.ExistsAsync( id ) ;
    return exists ? Ok() : NotFound() ;
  }

  /// <summary>
  ///   Updates a broker account.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] BrokerAccountRequest request )
  {
    var existing = await brokerAccountService.GetByIdAsync( id ) ;
    if ( existing is null )
    {
      return NotFound() ;
    }

    existing.Name = request.Name ;

    await brokerAccountService.UpdateAsync( existing ) ;
    return Ok( new BrokerAccountResponse( existing ) ) ;
  }

  /// <summary>
  ///   Soft-deletes a broker account.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await brokerAccountService.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
