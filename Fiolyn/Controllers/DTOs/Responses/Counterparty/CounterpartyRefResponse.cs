namespace Fiolyn.Controllers.DTOs.Responses.Counterparty ;

/// <summary>
///   Lightweight reference to a <see cref="Entities.Counterparty" /> embedded in lend / borrowing
///   responses. Mirrors the shape of <c>TransactionCategoryRefResponse</c>.
/// </summary>
public record CounterpartyRefResponse( long Id, string Name )
{
  public CounterpartyRefResponse( Entities.Counterparty c )
    : this( c.Id, c.Name )
  {
  }
}
