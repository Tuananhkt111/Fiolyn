using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.SpendAccount ;

/// <summary>
///   Response body for a spend account returned from a user-scoped endpoint.
///   The caller is always a member when this record is returned — caller identity
///   is carried on the JWT, not echoed in the payload.
/// </summary>
public record SpendAccountResponse(
  long Id,
  string Name,
  SpendAccountType Type,
  long Balance,
  DateTime Created,
  DateTime LastModified )
{
  public SpendAccountResponse( Entities.SpendAccount a )
    : this( a.Id, a.Name, a.Type, a.Balance, a.Created, a.LastModified )
  {
  }
}
