using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.SpendAccount ;

/// <summary>
///   Response body for a spend account. <c>AppUserId</c> carries the caller's user ID
///   (the caller is always a member when this record is returned from a user-scoped endpoint);
///   it is kept for frontend back-compat and is not a single "owner" — all members are equal.
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
  public SpendAccountResponse( Entities.SpendAccount a, long callerUserId )
    : this( a.Id, callerUserId, a.Name, a.Type, a.Balance, a.Created, a.LastModified )
  {
  }
}
