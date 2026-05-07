using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal.Helpers ;

/// <summary>
///   Shared write/read plumbing for transaction leaves that reference a per-user
///   <see cref="Counterparty" /> — currently <see cref="LendTransaction" /> and
///   <see cref="BorrowingTransaction" />. Sits between <see cref="StandardTransactionServiceBase{T}" />
///   and the leaf service: resolves the request's free-text counterparty name to a
///   <see cref="Counterparty" /> row owned by the caller (creating one on the fly when missing) and
///   eager-loads the nav on reads.
/// </summary>
public abstract class CounterpartyTransactionServiceBase<T>(
  IRepository<T> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository,
  IRepository<Counterparty> counterpartyRepository )
  : StandardTransactionServiceBase<T>( transactionRepository, spendRepository, categoryRepository )
  where T : TransactionBase, IHasCounterparty
{
  public async Task<T> CreateAsync( T transaction, long userId, string counterpartyName )
  {
    var counterparty = await CounterpartyResolver.ResolveOrCreateAsync(
      counterpartyRepository, counterpartyName, userId ) ;
    transaction.Counterparty = counterparty ;
    return await CreateAsync( transaction, userId ) ;
  }

  public async Task UpdateAsync( T transaction, long userId, string counterpartyName )
  {
    // Skip the resolver roundtrip when the name didn't change. The controller mutates a tracked
    // entity loaded via GetByIdForUserAsync, which eager-loads Counterparty (see BuildReadQuery
    // override), so transaction.Counterparty.Name is the pre-mutation value.
    if ( ! string.Equals(
          transaction.Counterparty.Name,
          counterpartyName.Trim(),
          StringComparison.OrdinalIgnoreCase ) )
    {
      var counterparty = await CounterpartyResolver.ResolveOrCreateAsync(
        counterpartyRepository, counterpartyName, userId ) ;
      transaction.Counterparty = counterparty ;
    }

    await UpdateAsync( transaction, userId ) ;
  }

  protected override IQueryable<T> BuildReadQuery( IQueryable<T> query ) =>
    base.BuildReadQuery( query ).Include( nameof( IHasCounterparty.Counterparty ) ) ;
}
