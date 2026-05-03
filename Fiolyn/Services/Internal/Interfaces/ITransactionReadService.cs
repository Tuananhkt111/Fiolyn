using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Unified, polymorphic read across every <see cref="TransactionBase" /> leaf. Powers
///   <c>GET /api/transactions</c>. Membership predicate unions <c>SpendAccount</c> membership and
///   (for stock rows only) <c>BrokerAccount</c> membership so a broker-only-but-not-spend user
///   still sees their stock rows in the unified history.
/// </summary>
public interface ITransactionReadService
{
  /// <summary>
  ///   Lists every active transaction the user has access to, ordered by <c>Date</c> descending.
  /// </summary>
  Task<List<TransactionBase>> GetAllForUserAsync( long userId ) ;

  /// <summary>
  ///   (Admin) Lists every active transaction without user-scoping. Optionally filter to a single
  ///   user's accessible rows.
  /// </summary>
  Task<List<TransactionBase>> GetAllAsync( long? userId = null ) ;
}
