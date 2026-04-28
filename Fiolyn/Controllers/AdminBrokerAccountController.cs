using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;
using Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Admin-only REST API for managing broker accounts across all users.
/// </summary>
[ApiController]
[Route( "api/admin/broker-accounts" )]
[Authorize( Policy = AuthConstants.AdminPolicyName )]
public class AdminBrokerAccountController(
  IBrokerAccountService brokerAccountService ) : ControllerBase
{
  /// <summary>
  ///   Lists all broker accounts across every user. Optionally filter to accounts a given user is a member of.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync( [FromQuery] long? userId = null )
  {
    var list = await brokerAccountService.GetAllAsync( userId ) ;
    return Ok( list.Select( b => new AdminBrokerAccountResponse( b ) ).ToList() ) ;
  }

  /// <summary>
  ///   Gets any broker account by ID (not scoped to caller).
  /// </summary>
  [HttpGet( "{id:long}", Name = "GetAdminBrokerAccountById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var account = await brokerAccountService.GetByIdAsync( id ) ;
    if ( account is null )
    {
      return NotFound() ;
    }

    return Ok( new AdminBrokerAccountResponse( account ) ) ;
  }

  /// <summary>
  ///   Creates a new broker account with the specified user as its initial member.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] AdminBrokerAccountCreateRequest request )
  {
    var account = new BrokerAccount
    {
      Name = request.Name,
      SpendAccountId = request.SpendAccountId
    } ;

    var created = await brokerAccountService.CreateAsync( account, request.AppUserId ) ;
    var withNav = await brokerAccountService.GetByIdAsync( created.Id ) ;
    return CreatedAtRoute( "GetAdminBrokerAccountById", new
    {
      id = created.Id
    }, new AdminBrokerAccountResponse( withNav! ) ) ;
  }

  /// <summary>
  ///   Updates any broker account (Name).
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] BrokerAccountRequest request )
  {
    var existing = await brokerAccountService.GetByIdAsync( id ) ;
    if ( existing is null )
    {
      return NotFound() ;
    }

    var oldSpendId = existing.SpendAccountId ;
    existing.Name = request.Name ;
    existing.SpendAccountId = request.SpendAccountId ;

    await brokerAccountService.UpdateAsync( existing ) ;

    if ( oldSpendId == request.SpendAccountId )
    {
      return Ok( new AdminBrokerAccountResponse( existing ) ) ;
    }

    var refreshed = await brokerAccountService.GetByIdAsync( id ) ;
    return Ok( new AdminBrokerAccountResponse( refreshed! ) ) ;
  }

  /// <summary>
  ///   Soft-deletes any broker account.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await brokerAccountService.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
