using Fiolyn.Common.Pagination ;
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
  public async Task<IActionResult> GetAllAsync( [FromQuery] PaginationQuery pagination )
  {
    var userId = User.GetUserId() ;
    var page = await spendAccountService.GetAllForUserAsync( userId, pagination ) ;
    return Ok( page.Map( a => new SpendAccountResponse( a ) ) ) ;
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
  ///   Updates a spend account's name and type. 404 if the caller is not a member.
  ///   Balance is intentionally not updatable here — every balance change flows through a
  ///   transaction (Income/Expense/Lend/Borrowing/Transfer/Adjustment/Stock) so the audit trail and
  ///   atomic SQL path are uniform.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] SpendAccountUpdateRequest request )
  {
    var updated = await spendAccountService.UpdateAsync( id, User.GetUserId(), request.Name, request.Type ) ;
    if ( updated is null )
    {
      return NotFound() ;
    }

    return Ok( new SpendAccountResponse( updated ) ) ;
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
  public async Task<IActionResult> GetMembersAsync( long id, [FromQuery] PaginationQuery pagination )
  {
    var page = await spendAccountService.GetMembersAsync( id, User.GetUserId(), pagination ) ;
    return Ok( page.Map( m => new SpendAccountMemberResponse( m ) ) ) ;
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
