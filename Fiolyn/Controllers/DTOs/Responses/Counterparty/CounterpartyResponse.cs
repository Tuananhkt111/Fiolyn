namespace Fiolyn.Controllers.DTOs.Responses.Counterparty ;

/// <summary>
///   Full response body for a <see cref="Entities.Counterparty" /> — used by the dedicated
///   <c>/api/counterparties</c> CRUD endpoints.
/// </summary>
public record CounterpartyResponse(
  long Id,
  string Name,
  DateTime Created,
  DateTime LastModified )
{
  public CounterpartyResponse( Entities.Counterparty c )
    : this( c.Id, c.Name, c.Created, c.LastModified )
  {
  }
}
