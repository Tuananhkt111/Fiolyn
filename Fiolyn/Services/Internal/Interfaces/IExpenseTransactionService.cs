using Fiolyn.Entities ;
using Fiolyn.Services.Internal.BaseServices ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Manages <see cref="ExpenseTransaction" /> records. User-scoped methods enforce
///   <see cref="SpendAccount" /> membership; "not a member" → 404. Each create / update accepts
///   an optional list of <see cref="TransactionShareInput" /> rows; non-empty lists are resolved
///   to per-user <see cref="Counterparty" /> rows and atomically materialised as paired
///   <see cref="LendTransaction" /> children linked back via
///   <see cref="TransactionBase.ParentTransactionId" />.
/// </summary>
public interface IExpenseTransactionService
{
  Task<ExpenseTransaction> CreateAsync(
    ExpenseTransaction transaction, long userId, IReadOnlyList<TransactionShareInput> shares ) ;

  Task UpdateAsync(
    ExpenseTransaction transaction, long userId, IReadOnlyList<TransactionShareInput> shares ) ;

  Task SoftDeleteAsync( long id, long userId ) ;
  Task SoftDeleteAsync( long id ) ;
  Task<List<ExpenseTransaction>> GetAllForUserAsync( long userId ) ;
  Task<ExpenseTransaction?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task<List<ExpenseTransaction>> GetAllAsync( long? userId = null ) ;
  Task<ExpenseTransaction?> GetByIdAsync( long id ) ;
}
