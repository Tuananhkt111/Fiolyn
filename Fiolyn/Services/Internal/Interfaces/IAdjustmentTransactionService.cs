using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Manages <see cref="AdjustmentTransaction" /> records. Replaces the legacy
///   <c>POST /spend-accounts/{id}/balance-adjustments</c> endpoint with a tracked, audited row.
///   Unlike the other simple leaves, the <c>Amount</c> is signed by the caller (the controller
///   passes the user's <c>delta</c> through unchanged).
/// </summary>
public interface IAdjustmentTransactionService
{
  Task<AdjustmentTransaction> CreateAsync( AdjustmentTransaction transaction, long userId ) ;
  Task UpdateAsync( AdjustmentTransaction transaction, long userId ) ;
  Task SoftDeleteAsync( long id, long userId ) ;
  Task SoftDeleteAsync( long id ) ;
  Task<List<AdjustmentTransaction>> GetAllForUserAsync( long userId ) ;
  Task<AdjustmentTransaction?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task<List<AdjustmentTransaction>> GetAllAsync( long? userId = null ) ;
  Task<AdjustmentTransaction?> GetByIdAsync( long id ) ;
}
