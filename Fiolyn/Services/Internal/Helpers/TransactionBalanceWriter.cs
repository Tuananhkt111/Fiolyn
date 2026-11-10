using Fiolyn.Data.Repositories ;
using Fiolyn.Infrastructure.Exceptions ;
using Microsoft.EntityFrameworkCore ;
using Npgsql ;

namespace Fiolyn.Services.Internal.Helpers ;

/// <summary>
///   Atomic balance-write helper. Wraps <see cref="IRepository{T}.SaveAsync" /> in the
///   <c>DbUpdateConcurrencyException</c> and unique-violation <c>→ ConflictException</c> mappings that every transaction-side
///   service needs (loads tracked <c>SpendAccount</c>, mutates <c>Balance</c>, saves once,
///   relies on <c>[ConcurrencyCheck]</c> on <c>Balance</c> to detect concurrent writers).
/// </summary>
internal static class TransactionBalanceWriter
{
  /// <summary>
  ///   Saves changes through the supplied repository. Translates a concurrency failure into a
  ///   <see cref="ConflictException" /> naming the spend account(s) involved.
  /// </summary>
  internal static async Task SaveWithConcurrencyGuardAsync<T>(
    IRepository<T> repository, params long[] spendAccountIds ) where T : class
  {
    try
    {
      await repository.SaveAsync() ;
    }
    catch ( DbUpdateConcurrencyException )
    {
      var ids = spendAccountIds.Length > 0 ? string.Join( ", ", spendAccountIds ) : "(unknown)" ;
      throw new ConflictException(
        $"SpendAccount [{ids}] balance was modified concurrently — retry the operation." ) ;
    }
    catch ( DbUpdateException ex )
      when ( ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } )
    {
      // A row staged in the same save (a new counterparty) collided with one a concurrent request
      // just created. Same mapping as every other unique violation (docs/rules/ef-pitfalls.md).
      throw new ConflictException( "A record with the same unique value was just created — retry the operation." ) ;
    }
  }
}
