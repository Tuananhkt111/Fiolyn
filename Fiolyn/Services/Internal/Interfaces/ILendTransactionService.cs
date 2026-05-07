using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Manages <see cref="LendTransaction" /> records. User-scoped methods enforce
///   <see cref="SpendAccount" /> membership; "not a member" → 404. The counterparty is supplied as a
///   free-text name on create/update; the service resolves to (or creates) a per-user
///   <see cref="Counterparty" /> row owned by the caller.
/// </summary>
public interface ILendTransactionService
{
  Task<LendTransaction> CreateAsync( LendTransaction transaction, long userId, string counterpartyName ) ;
  Task UpdateAsync( LendTransaction transaction, long userId, string counterpartyName ) ;
  Task SoftDeleteAsync( long id, long userId ) ;
  Task SoftDeleteAsync( long id ) ;
  Task<List<LendTransaction>> GetAllForUserAsync( long userId ) ;
  Task<LendTransaction?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task<List<LendTransaction>> GetAllAsync( long? userId = null ) ;
  Task<LendTransaction?> GetByIdAsync( long id ) ;
}
