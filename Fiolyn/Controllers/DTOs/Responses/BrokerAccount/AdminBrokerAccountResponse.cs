namespace Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;

/// <summary>
///   Admin response body for a broker account, including the full list of member users.
/// </summary>
public record AdminBrokerAccountResponse(
  long Id,
  string Name,
  long SpendAccountId,
  string SpendAccountName,
  IReadOnlyList<BrokerAccountMemberResponse> Members,
  DateTime Created,
  DateTime LastModified )
{
  public AdminBrokerAccountResponse( Entities.BrokerAccount b )
    : this(
      b.Id,
      b.Name,
      b.SpendAccountId,
      b.SpendAccount.Name,
      b.Members
        .OrderBy( m => m.JoinedAt )
        .Select( m => new BrokerAccountMemberResponse( m ) )
        .ToList(),
      b.Created,
      b.LastModified )
  {
  }
}
