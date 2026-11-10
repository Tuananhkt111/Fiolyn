using System.ComponentModel.DataAnnotations ;
using Fiolyn.Services.Internal.BaseServices ;

namespace Fiolyn.Controllers.DTOs.Requests.Shared ;

/// <summary>
///   One slot in the optional <c>shares</c> array on income / expense create / update bodies.
///   The free-text <c>counterpartyName</c> is resolved (or created) per-user; the
///   <c>percentage</c> is rounded server-side to 2 decimal places (half-away-from-zero) and only
///   then range-checked against [0.01, 100], so it carries no <c>[Range]</c> here — a DTO range would
///   reject values such as 0.005 that round into range. The sum across all shares on a single
///   transaction must be ≤ 100 (validated server-side).
/// </summary>
public record TransactionShareRequest(
  [Required] [MaxLength( 100 )] string CounterpartyName,
  [Required] double Percentage ) ;

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
