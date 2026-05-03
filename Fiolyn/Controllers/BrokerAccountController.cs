using Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;
using Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;
using Fiolyn.Entities ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing broker accounts the caller is a member of.
/// </summary>
[ApiController]
[Route( "api/broker-accounts" )]
[Authorize]
public class BrokerAccountController(
  IBrokerAccountService brokerAccountService ) : ControllerBase
{
  /// <summary>
  ///   Creates a new broker account with the caller as its first member.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] BrokerAccountRequest request )
  {
    var userId = User.GetUserId() ;
    var brokerAccount = new BrokerAccount
    {
      Name = request.Name,
      SpendAccountId = request.SpendAccountId
    } ;

    var created = await brokerAccountService.CreateAsync( brokerAccount, userId ) ;
    var withNav = await brokerAccountService.GetByIdForUserAsync( created.Id, userId ) ;
    return CreatedAtRoute( "GetBrokerAccountById", new
    {
      id = created.Id
    }, new BrokerAccountResponse( withNav! ) ) ;
  }

  /// <summary>
  ///   Lists broker accounts the caller is a member of.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync()
  {
    var list = await brokerAccountService.GetAllForUserAsync( User.GetUserId() ) ;
    return Ok( list.Select( b => new BrokerAccountResponse( b ) ).ToList() ) ;
  }

  /// <summary>
  ///   Gets a broker account by ID. 404 if the caller is not a member.
  /// </summary>
  [HttpGet( "{id:long}", Name = "GetBrokerAccountById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var brokerAccount = await brokerAccountService.GetByIdForUserAsync( id, User.GetUserId() ) ;
    if ( brokerAccount is null )
    {
      return NotFound() ;
    }

    return Ok( new BrokerAccountResponse( brokerAccount ) ) ;
  }

  /// <summary>
  ///   Checks whether an active broker account exists and caller is a member.
  /// </summary>
  [HttpHead( "{id:long}" )]
  public async Task<IActionResult> ExistsAsync( long id )
  {
    var exists = await brokerAccountService.ExistsForUserAsync( id, User.GetUserId() ) ;
    return exists ? Ok() : NotFound() ;
  }

  /// <summary>
  ///   Updates a broker account. 404 if the caller is not a member.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] BrokerAccountRequest request )
  {
    var updated = await brokerAccountService.UpdateAsync(
      id, User.GetUserId(), request.Name, request.SpendAccountId ) ;
    if ( updated is null )
    {
      return NotFound() ;
    }

    return Ok( new BrokerAccountResponse( updated ) ) ;
  }

  /// <summary>
  ///   Soft-deletes a broker account. 404 if the caller is not a member.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await brokerAccountService.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }

  /// <summary>
  ///   Lists members of the account. 404 if the caller is not a member.
  /// </summary>
  [HttpGet( "{id:long}/members" )]
  public async Task<IActionResult> GetMembersAsync( long id )
  {
    var members = await brokerAccountService.GetMembersAsync( id, User.GetUserId() ) ;
    return Ok( members.Select( m => new BrokerAccountMemberResponse( m ) ).ToList() ) ;
  }

  /// <summary>
  ///   Adds a user to the account by email. Idempotent — returns 200 + member list if already a member.
  ///   404 if caller is not a member or the target email has no registered user.
  /// </summary>
  [HttpPost( "{id:long}/members" )]
  public async Task<IActionResult> AddMemberAsync( long id, [FromBody] AddMemberRequest request )
  {
    var members = await brokerAccountService.AddMemberAsync( id, User.GetUserId(), request.Email ) ;
    return Ok( members.Select( m => new BrokerAccountMemberResponse( m ) ).ToList() ) ;
  }

  /// <summary>
  ///   Removes a user from the account. 404 if caller or target is not a member.
  ///   400 if removing this user would leave the account with zero members.
  /// </summary>
  [HttpDelete( "{id:long}/members/{userId:long}" )]
  public async Task<IActionResult> RemoveMemberAsync( long id, long userId )
  {
    await brokerAccountService.RemoveMemberAsync( id, User.GetUserId(), userId ) ;
    return NoContent() ;
  }
}
