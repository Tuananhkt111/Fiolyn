using Fiolyn.Controllers.DTOs.Requests.SpendAccount ;
using Fiolyn.Controllers.DTOs.Responses.SpendAccount ;
using Fiolyn.Entities ;
using Fiolyn.Extensions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authorization ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
///   REST API for managing the caller's spend accounts.
/// </summary>
[ApiController]
[Route( "api/spend-accounts" )]
[Authorize]
public class SpendAccountController(
  ISpendAccountService spendAccountService ) : ControllerBase
{
  /// <summary>
  ///   Creates a new spend account owned by the caller.
  /// </summary>
  [HttpPost]
  public async Task<IActionResult> CreateAsync( [FromBody] SpendAccountRequest request )
  {
    var account = new SpendAccount
    {
      AppUserId = User.GetUserId(),
      Name = request.Name,
      Type = request.Type,
      Balance = request.Balance
    } ;

    var created = await spendAccountService.CreateAsync( account ) ;
    return CreatedAtRoute( "GetSpendAccountById", new
    {
      id = created.Id
    }, new SpendAccountResponse( created ) ) ;
  }

  /// <summary>
  ///   Lists the caller's spend accounts.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> GetAllAsync()
  {
    var list = await spendAccountService.GetAllForUserAsync( User.GetUserId() ) ;
    return Ok( list.Select( a => new SpendAccountResponse( a ) ).ToList() ) ;
  }

  /// <summary>
  ///   Gets one of the caller's spend accounts by ID.
  /// </summary>
  [HttpGet( "{id:long}", Name = "GetSpendAccountById" )]
  public async Task<IActionResult> GetByIdAsync( long id )
  {
    var account = await spendAccountService.GetByIdForUserAsync( id, User.GetUserId() ) ;
    if ( account is null )
    {
      return NotFound() ;
    }

    return Ok( new SpendAccountResponse( account ) ) ;
  }

  /// <summary>
  ///   Updates one of the caller's spend accounts.
  /// </summary>
  [HttpPut( "{id:long}" )]
  public async Task<IActionResult> UpdateAsync( long id, [FromBody] SpendAccountRequest request )
  {
    var existing = await spendAccountService.GetByIdForUserAsync( id, User.GetUserId() ) ;
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
  ///   Soft-deletes one of the caller's spend accounts.
  /// </summary>
  [HttpDelete( "{id:long}" )]
  public async Task<IActionResult> DeleteAsync( long id )
  {
    await spendAccountService.SoftDeleteAsync( id, User.GetUserId() ) ;
    return NoContent() ;
  }
}
