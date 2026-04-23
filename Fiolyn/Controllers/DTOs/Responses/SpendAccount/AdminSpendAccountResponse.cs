using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.SpendAccount ;

/// <summary>
///   Admin response body for a spend account, including the owning user's identity.
/// </summary>
public record AdminSpendAccountResponse(
  long Id,
  long AppUserId,
  string AppUserEmail,
  string? AppUserDisplayName,
  string Name,
  SpendAccountType Type,
  long Balance,
  DateTime Created,
  DateTime LastModified )
{
  public AdminSpendAccountResponse( Entities.SpendAccount a )
    : this(
      a.Id, a.AppUserId, a.AppUser.Email, a.AppUser.DisplayName,
      a.Name, a.Type, a.Balance, a.Created, a.LastModified )
  {
  }
}
