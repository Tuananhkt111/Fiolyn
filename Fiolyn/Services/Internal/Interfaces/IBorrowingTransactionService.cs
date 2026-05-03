using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Manages <see cref="BorrowingTransaction" /> records. User-scoped methods enforce
///   <see cref="SpendAccount" /> membership; "not a member" → 404.
/// </summary>
public interface IBorrowingTransactionService
{
  Task<BorrowingTransaction> CreateAsync( BorrowingTransaction transaction, long userId ) ;
  Task UpdateAsync( BorrowingTransaction transaction, long userId ) ;
  Task SoftDeleteAsync( long id, long userId ) ;
  Task SoftDeleteAsync( long id ) ;
  Task<List<BorrowingTransaction>> GetAllForUserAsync( long userId ) ;
  Task<BorrowingTransaction?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task<List<BorrowingTransaction>> GetAllAsync( long? userId = null ) ;
  Task<BorrowingTransaction?> GetByIdAsync( long id ) ;
}
