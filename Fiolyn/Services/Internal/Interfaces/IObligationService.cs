using Fiolyn.Common.Pagination ;
using Fiolyn.Controllers.DTOs.Responses.Obligation ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Aggregates per-counterparty receivables (sum of <c>LendTransaction</c> magnitudes) and
///   payables (sum of <c>BorrowingTransaction</c> magnitudes) for the calling user. Auto-paired
///   share children are regular Lend / Borrow rows so they're counted by the same aggregate.
/// </summary>
public interface IObligationService
{
  Task<PagedResult<ObligationResponse>> GetAllForUserAsync( long userId, PaginationQuery pagination ) ;
}
