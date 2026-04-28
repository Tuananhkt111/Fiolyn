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
  public async Task<IActionResult> GetAllAsync( [FromQuery] long? userId = null )
  {
    var list = await spendAccountService.GetAllAsync( userId ) ;
    return Ok( list.Select( a => new AdminSpendAccountResponse( a ) ).ToList() ) ;
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
  ///   use POST /balance-adjustments so the change goes through the atomic SQL balance path.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] SpendAccountUpdateRequest request )
  {
    var existing = await spendAccountService.GetByIdAsync( id ) ;
    if ( existing is null )
    {
      return NotFound() ;
    }

    existing.Name = request.Name ;
    existing.Type = request.Type ;

    await spendAccountService.UpdateAsync( existing ) ;
    return Ok( new AdminSpendAccountResponse( existing ) ) ;
  }

  /// <summary>
  ///   Atomically adjusts any spend account's balance by a signed delta.
  /// </summary>
  [HttpPost( "{id:long}/balance-adjustments" )]
  public async Task<IActionResult> AdjustBalanceAsync(
    long id,
    [FromBody] SpendAccountBalanceAdjustmentRequest request )
  {
    var newBalance = await spendAccountService.AdjustBalanceAsync( id, request.Delta ) ;
    return Ok( new SpendAccountBalanceResponse( id, newBalance ) ) ;
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
