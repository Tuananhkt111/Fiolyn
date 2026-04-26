namespace Fiolyn.Data.Repositories ;

/// <summary>
///   Default <see cref="IUnitOfWork" /> backed by the scoped <see cref="AppDbContext" />.
///   Rollback is handled by `await using` disposal — if the action throws, the transaction is
///   disposed without ever calling <c>CommitAsync</c>, which Npgsql treats as a rollback.
/// </summary>
public class UnitOfWork( AppDbContext dbContext ) : IUnitOfWork
{
  /// <inheritdoc />
  public async Task ExecuteInTransactionAsync( Func<Task> action )
  {
    await using var transaction = await dbContext.Database.BeginTransactionAsync() ;
    await action() ;
    await transaction.CommitAsync() ;
  }
}
