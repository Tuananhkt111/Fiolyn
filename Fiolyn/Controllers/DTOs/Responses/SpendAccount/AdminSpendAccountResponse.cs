using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.SpendAccount ;

/// <summary>
///   Admin response body for a spend account, including the full list of member users.
/// </summary>
public record AdminSpendAccountResponse(
  long Id,
  string Name,
  SpendAccountType Type,
  long Balance,
  IReadOnlyList<SpendAccountMemberResponse> Members,
  DateTime Created,
  DateTime LastModified )
{
  public AdminSpendAccountResponse( Entities.SpendAccount a )
    : this(
      a.Id,
      a.Name,
      a.Type,
      a.Balance,
      a.Members
        .OrderBy( m => m.JoinedAt )
        .Select( m => new SpendAccountMemberResponse( m ) )
        .ToList(),
      a.Created,
      a.LastModified )
  {
  }
}
