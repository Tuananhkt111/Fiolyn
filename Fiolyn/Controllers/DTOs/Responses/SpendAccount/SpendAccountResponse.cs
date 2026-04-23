using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.SpendAccount ;

/// <summary>
///   Response body for a spend account.
/// </summary>
public record SpendAccountResponse(
  long Id,
  long AppUserId,
  string Name,
  SpendAccountType Type,
  long Balance,
  DateTime Created,
  DateTime LastModified )
{
  public SpendAccountResponse( Entities.SpendAccount a )
    : this( a.Id, a.AppUserId, a.Name, a.Type, a.Balance, a.Created, a.LastModified )
  {
  }
}
