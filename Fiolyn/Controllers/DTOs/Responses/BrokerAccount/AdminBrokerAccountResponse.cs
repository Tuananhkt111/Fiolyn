namespace Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;

/// <summary>
///   Admin response body for a broker account, including the full list of member users.
/// </summary>
public record AdminBrokerAccountResponse(
  long Id,
  string Name,
  IReadOnlyList<BrokerAccountMemberResponse> Members,
  DateTime Created,
  DateTime LastModified )
{
  public AdminBrokerAccountResponse( Entities.BrokerAccount b )
    : this(
      b.Id,
      b.Name,
      b.Members
        .Where( m => ! m.AppUser.IsDeleted )
        .OrderBy( m => m.JoinedAt )
        .Select( m => new BrokerAccountMemberResponse( m ) )
        .ToList(),
      b.Created,
      b.LastModified )
  {
  }
}
