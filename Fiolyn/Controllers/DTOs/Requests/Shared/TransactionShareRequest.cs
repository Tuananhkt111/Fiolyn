using System.ComponentModel.DataAnnotations ;
using Fiolyn.Services.Internal.BaseServices ;

namespace Fiolyn.Controllers.DTOs.Requests.Shared ;

/// <summary>
///   One slot in the optional <c>shares</c> array on income / expense create / update bodies.
///   The free-text <c>counterpartyName</c> is resolved (or created) per-user; the
///   <c>percentage</c> is a whole percent slice. The sum across all shares on a single
///   transaction must be ≤ 100 (validated server-side).
/// </summary>
public record TransactionShareRequest(
  [Required] [MaxLength( 100 )] string CounterpartyName,
  [Required] [Range( 1, 100 )] int Percentage ) ;

public static class TransactionShareRequestExtensions
{
  extension( IReadOnlyList<TransactionShareRequest>? shares )
  {
    public IReadOnlyList<TransactionShareInput> ToShareInputs() =>
      shares is null
        ? []
        : shares.Select( s => new TransactionShareInput( s.CounterpartyName, s.Percentage ) ).ToList() ;
  }
}
