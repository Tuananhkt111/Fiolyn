using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Manages a caller's private <see cref="Counterparty" /> contact list. All methods are scoped
///   by <c>AppUserId</c> — "not owned by caller" surfaces as 404. Soft-delete is blocked while any
///   active lend/borrow row references the counterparty.
/// </summary>
public interface ICounterpartyService
{
  Task<List<Counterparty>> GetAllForUserAsync( long userId ) ;
  Task<Counterparty?> GetByIdForUserAsync( long id, long userId ) ;
  Task<Counterparty> CreateAsync( string name, long userId ) ;
  Task<Counterparty?> UpdateAsync( long id, long userId, string name ) ;
  Task SoftDeleteAsync( long id, long userId ) ;
}
