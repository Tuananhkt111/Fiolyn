using Fiolyn.Entities ;

namespace Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;

/// <summary>
///   Response body for a single member of a shared broker account.
/// </summary>
public record BrokerAccountMemberResponse(
  long AppUserId,
  string Email,
  string? DisplayName,
  DateTime JoinedAt )
{
  public BrokerAccountMemberResponse( BrokerAccountMember m )
    : this( m.AppUserId, m.AppUser.Email, m.AppUser.DisplayName, m.JoinedAt )
  {
  }
}
