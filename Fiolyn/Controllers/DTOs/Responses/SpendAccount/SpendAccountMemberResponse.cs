using Fiolyn.Entities ;

namespace Fiolyn.Controllers.DTOs.Responses.SpendAccount ;

/// <summary>
///   Response body for a single member of a shared spend account.
/// </summary>
public record SpendAccountMemberResponse(
  long AppUserId,
  string Email,
  string? DisplayName,
  DateTime JoinedAt )
{
  public SpendAccountMemberResponse( SpendAccountMember m )
    : this( m.AppUserId, m.AppUser.Email, m.AppUser.DisplayName, m.JoinedAt )
  {
  }
}
