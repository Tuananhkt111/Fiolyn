using Fiolyn.Controllers.DTOs.Requests.SpendAccount ;
using Fiolyn.Controllers.DTOs.Responses.SpendAccount ;
using Fiolyn.Entities ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing spend accounts the caller is a member of.
/// </summary>
[ApiController]
[Route( "api/spend-accounts" )]
[Authorize]
public class SpendAccountController(
  ISpendAccountService spendAccountService ) : ControllerBase
{
  /// <summary>
  ///   Creates a new spend account with the caller as its first member.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] SpendAccountRequest request )
  {
    var userId = User.GetUserId() ;
    var account = new SpendAccount
    {
      Name = request.Name,
      Type = request.Type,
      Balance = request.Balance
    } ;

    var created = await spendAccountService.CreateAsync( account, userId ) ;
    return CreatedAtRoute( "GetSpendAccountById", new
    {
      id = created.Id
    }, new SpendAccountResponse( created ) ) ;
  }

  /// <summary>
  ///   Lists spend accounts the caller is a member of.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync()
  {
    var userId = User.GetUserId() ;
    var list = await spendAccountService.GetAllForUserAsync( userId ) ;
    return Ok( list.Select( a => new SpendAccountResponse( a ) ).ToList() ) ;
  }

  /// <summary>
  ///   Gets a single spend account by ID. 404 if the caller is not a member.
  /// </summary>
  [HttpGet( "{id:long}", Name = "GetSpendAccountById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var userId = User.GetUserId() ;
    var account = await spendAccountService.GetByIdForUserAsync( id, userId ) ;
    if ( account is null )
    {
      return NotFound() ;
    }

    return Ok( new SpendAccountResponse( account ) ) ;
  }

  /// <summary>
  ///   Updates a spend account. 404 if the caller is not a member.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] SpendAccountRequest request )
  {
    var userId = User.GetUserId() ;
    var existing = await spendAccountService.GetByIdForUserAsync( id, userId ) ;
    if ( existing is null )
    {
      return NotFound() ;
    }

    existing.Name = request.Name ;
    existing.Type = request.Type ;
    existing.Balance = request.Balance ;

    await spendAccountService.UpdateAsync( existing ) ;
    return Ok( new SpendAccountResponse( existing ) ) ;
  }

  /// <summary>
  ///   Soft-deletes a spend account. 404 if the caller is not a member.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await spendAccountService.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }

  /// <summary>
  ///   Lists members of the account. 404 if the caller is not a member.
  /// </summary>
  [HttpGet( "{id:long}/members" )]
  public async Task<IActionResult> GetMembersAsync( long id )
  {
    var members = await spendAccountService.GetMembersAsync( id, User.GetUserId() ) ;
    return Ok( members.Select( m => new SpendAccountMemberResponse( m ) ).ToList() ) ;
  }

  /// <summary>
  ///   Adds a user to the account by email. Idempotent — returns 200 + member list if already a member.
  ///   404 if caller is not a member or the target email has no registered user.
  /// </summary>
  [HttpPost( "{id:long}/members" )]
  public async Task<IActionResult> AddMemberAsync( long id, [FromBody] AddMemberRequest request )
  {
    var members = await spendAccountService.AddMemberAsync( id, User.GetUserId(), request.Email ) ;
    return Ok( members.Select( m => new SpendAccountMemberResponse( m ) ).ToList() ) ;
  }

  /// <summary>
  ///   Removes a user from the account. 404 if caller or target is not a member.
  ///   400 if removing this user would leave the account with zero members.
  /// </summary>
  [HttpDelete( "{id:long}/members/{userId:long}" )]
  public async Task<IActionResult> RemoveMemberAsync( long id, long userId )
  {
    await spendAccountService.RemoveMemberAsync( id, User.GetUserId(), userId ) ;
    return NoContent() ;
  }
}
