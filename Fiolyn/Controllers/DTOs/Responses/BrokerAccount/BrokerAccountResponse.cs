namespace Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;

/// <summary>
///   Response body for a broker account.
/// </summary>
public record BrokerAccountResponse(
  long Id,
  string Name,
  long SpendAccountId,
  string SpendAccountName,
  DateTime Created,
  DateTime LastModified )
{
  public BrokerAccountResponse( Entities.BrokerAccount b )
    : this(
      b.Id,
      b.Name,
      b.SpendAccountId,
      b.SpendAccount.Name,
      b.Created,
      b.LastModified )
  {
  }
}
