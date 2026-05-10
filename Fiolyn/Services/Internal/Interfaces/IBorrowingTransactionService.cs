using Fiolyn.Common.Pagination ;
using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Manages <see cref="BorrowingTransaction" /> records. User-scoped methods enforce
///   <see cref="SpendAccount" /> membership; "not a member" → 404. The counterparty is supplied as a
///   free-text name on create/update; the service resolves to (or creates) a per-user
///   <see cref="Counterparty" /> row owned by the caller.
/// </summary>
public interface IBorrowingTransactionService
{
  Task<BorrowingTransaction> CreateAsync( BorrowingTransaction transaction, long userId, string counterpartyName ) ;
  Task UpdateAsync( BorrowingTransaction transaction, long userId, string counterpartyName ) ;
  Task SoftDeleteAsync( long id, long userId ) ;
  Task SoftDeleteAsync( long id ) ;
  Task<PagedResult<BorrowingTransaction>> GetAllForUserAsync( long userId, PaginationQuery pagination ) ;
  Task<BorrowingTransaction?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task<PagedResult<BorrowingTransaction>> GetAllAsync( PaginationQuery pagination, long? userId = null ) ;
  Task<BorrowingTransaction?> GetByIdAsync( long id ) ;
}
