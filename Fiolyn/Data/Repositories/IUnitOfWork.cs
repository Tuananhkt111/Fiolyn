namespace Fiolyn.Data.Repositories ;

/// <summary>
///   Unit-of-work primitive for the shared <see cref="AppDbContext" />. Used when a single logical
///   operation needs to combine `ExecuteUpdate*` calls (which run their own SQL outside
///   <c>SaveChanges</c>) with normal tracked-entity writes — both must land or neither.
///   Distinct from <see cref="IRepository{T}" /> on purpose: transactions are a context-level concern
///   with no entity type, and putting them on the per-T repository forces an arbitrary choice of
///   which closed generic to call.
/// </summary>
public interface IUnitOfWork
{
  /// <summary>
  ///   Runs <paramref name="action" /> inside an explicit DB transaction. The transaction commits
  ///   on success and rolls back if <paramref name="action" /> throws.
  /// </summary>
  /// <param name="action">The work to run inside the transaction.</param>
  Task ExecuteInTransactionAsync( Func<Task> action ) ;
}
