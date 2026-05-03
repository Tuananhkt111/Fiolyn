using Fiolyn.Entities ;

namespace Fiolyn.Controllers.DTOs.Responses.Transaction ;

/// <summary>
///   Compact payer projection for transaction responses.
/// </summary>
public record TransactionPayerResponse( long Id, string Email, string? DisplayName )
{
  public TransactionPayerResponse( AppUser u ) : this( u.Id, u.Email, u.DisplayName )
  {
  }
}
