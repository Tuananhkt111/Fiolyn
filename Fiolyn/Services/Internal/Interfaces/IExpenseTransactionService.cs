using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Manages <see cref="ExpenseTransaction" /> records. User-scoped methods enforce
///   <see cref="SpendAccount" /> membership; "not a member" → 404.
/// </summary>
public interface IExpenseTransactionService
{
  Task<ExpenseTransaction> CreateAsync( ExpenseTransaction transaction, long userId ) ;
  Task UpdateAsync( ExpenseTransaction transaction, long userId ) ;
  Task SoftDeleteAsync( long id, long userId ) ;
  Task SoftDeleteAsync( long id ) ;
  Task<List<ExpenseTransaction>> GetAllForUserAsync( long userId ) ;
  Task<ExpenseTransaction?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task<List<ExpenseTransaction>> GetAllAsync( long? userId = null ) ;
  Task<ExpenseTransaction?> GetByIdAsync( long id ) ;
}
