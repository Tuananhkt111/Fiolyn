using Fiolyn.Entities ;
using Fiolyn.Services.Internal.BaseServices ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Manages <see cref="IncomeTransaction" /> records. User-scoped methods enforce
///   <see cref="SpendAccount" /> membership; "not a member" → 404. Each create / update accepts
///   an optional list of <see cref="TransactionShareInput" /> rows; non-empty lists are resolved
///   to per-user <see cref="Counterparty" /> rows and atomically materialised as paired
///   <see cref="BorrowingTransaction" /> children linked back via
///   <see cref="TransactionBase.ParentTransactionId" />.
/// </summary>
public interface IIncomeTransactionService
{
  Task<IncomeTransaction> CreateAsync(
    IncomeTransaction transaction, long userId, IReadOnlyList<TransactionShareInput> shares ) ;

  Task UpdateAsync(
    IncomeTransaction transaction, long userId, IReadOnlyList<TransactionShareInput> shares ) ;

  Task SoftDeleteAsync( long id, long userId ) ;
  Task SoftDeleteAsync( long id ) ;
  Task<List<IncomeTransaction>> GetAllForUserAsync( long userId ) ;
  Task<IncomeTransaction?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task<List<IncomeTransaction>> GetAllAsync( long? userId = null ) ;
  Task<IncomeTransaction?> GetByIdAsync( long id ) ;
}
