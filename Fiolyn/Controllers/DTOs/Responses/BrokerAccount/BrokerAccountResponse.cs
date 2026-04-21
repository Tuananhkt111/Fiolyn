namespace Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;

/// <summary>
///   Response body for a broker account.
/// </summary>
public record BrokerAccountResponse(
  long Id,
  string Name,
  DateTime Created,
  DateTime LastModified )
{
  public BrokerAccountResponse( Entities.BrokerAccount b )
    : this( b.Id, b.Name, b.Created, b.LastModified )
  {
  }
}
