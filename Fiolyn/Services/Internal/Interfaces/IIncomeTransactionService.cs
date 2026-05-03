using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Manages <see cref="IncomeTransaction" /> records. User-scoped methods enforce
///   <see cref="SpendAccount" /> membership; "not a member" → 404.
/// </summary>
public interface IIncomeTransactionService
{
  Task<IncomeTransaction> CreateAsync( IncomeTransaction transaction, long userId ) ;
  Task UpdateAsync( IncomeTransaction transaction, long userId ) ;
  Task SoftDeleteAsync( long id, long userId ) ;
  Task SoftDeleteAsync( long id ) ;
  Task<List<IncomeTransaction>> GetAllForUserAsync( long userId ) ;
  Task<IncomeTransaction?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task<List<IncomeTransaction>> GetAllAsync( long? userId = null ) ;
  Task<IncomeTransaction?> GetByIdAsync( long id ) ;
}
