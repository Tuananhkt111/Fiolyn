using System.ComponentModel.DataAnnotations ;
using Fiolyn.Services.Internal.BaseServices ;

namespace Fiolyn.Controllers.DTOs.Requests.Shared ;

/// <summary>
///   One slot in the optional <c>shares</c> array on income / expense create / update bodies.
///   The free-text <c>counterpartyName</c> is resolved (or created) per-user; the
///   <c>percentage</c> is a slice in [0.01, 100] with up to 2 decimal places — values with
///   more precision are silently rounded server-side (half-away-from-zero). The sum across
///   all shares on a single transaction must be ≤ 100 (validated server-side).
/// </summary>
public record TransactionShareRequest(
  [Required] [MaxLength( 100 )] string CounterpartyName,
  [Required] [Range( 0.01, 100 )] double Percentage ) ;

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
