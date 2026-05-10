using Fiolyn.Common.Pagination ;
using Fiolyn.Constants ;
using Fiolyn.Controllers.DTOs.Requests.SpendAccount ;
using Fiolyn.Controllers.DTOs.Responses.SpendAccount ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   Admin-only REST API for managing spend accounts across all users.
/// </summary>
[ApiController]
[Route( "api/admin/spend-accounts" )]
[Authorize( Policy = AuthConstants.AdminPolicyName )]
public class AdminSpendAccountController(
  ISpendAccountService spendAccountService ) : ControllerBase
{
  /// <summary>
  ///   Lists all spend accounts across every user. Optionally filter to accounts a given user is a member of.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync(
    [FromQuery] PaginationQuery pagination, [FromQuery] long? userId = null )
  {
    var page = await spendAccountService.GetAllAsync( pagination, userId ) ;
    return Ok( page.Map( a => new AdminSpendAccountResponse( a ) ) ) ;
  }

  /// <summary>
  ///   Gets any spend account by ID (not scoped to caller).
  /// </summary>
  [HttpGet( "{id:long}", Name = "GetAdminSpendAccountById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var account = await spendAccountService.GetByIdAsync( id ) ;
    if ( account is null )
    {
      return NotFound() ;
    }

    return Ok( new AdminSpendAccountResponse( account ) ) ;
  }

  /// <summary>
  ///   Creates a new spend account with the specified user as its initial member.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] AdminSpendAccountCreateRequest request )
  {
    var account = new SpendAccount
    {
      Name = request.Name,
      Type = request.Type,
      Balance = request.Balance
    } ;

    var created = await spendAccountService.CreateAsync( account, request.AppUserId ) ;
    var withNav = await spendAccountService.GetByIdAsync( created.Id ) ;
    return CreatedAtRoute( "GetAdminSpendAccountById", new
    {
      id = created.Id
    }, new AdminSpendAccountResponse( withNav! ) ) ;
  }

  /// <summary>
  ///   Updates any spend account's name and type. Balance is intentionally not updatable here —
  ///   every balance change flows through an <c>AdjustmentTransaction</c> (or another transaction
  ///   leaf) so the audit trail and atomic SQL path are uniform.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] SpendAccountUpdateRequest request )
  {
    var updated = await spendAccountService.UpdateAsync( id, request.Name, request.Type ) ;
    if ( updated is null )
    {
      return NotFound() ;
    }

    return Ok( new AdminSpendAccountResponse( updated ) ) ;
  }

  /// <summary>
  ///   Soft-deletes any spend account.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await spendAccountService.SoftDeleteAsync( id ) ;
    return NoContent() ;
  }
}
