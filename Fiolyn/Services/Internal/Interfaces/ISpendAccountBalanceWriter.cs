namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Writes atomic relative deltas to <see cref="Entities.SpendAccount" />.<c>Balance</c>. Intended for
///   the auto-balance path on stock transactions, where two concurrent writers must not lose updates
///   via a read-modify-write race. Implementations issue `Balance = Balance + @delta` at SQL level
///   and bypass the EF change tracker; <c>LastModified</c> is bumped manually since
///   <c>SaveChanges</c>'s `IAuditable` plumbing is not on this code path.
/// </summary>
public interface ISpendAccountBalanceWriter
{
  /// <summary>
  ///   Adds <paramref name="delta" /> (which may be negative) to the active spend account's balance
  ///   atomically, in a single SQL statement. Throws <see cref="KeyNotFoundException" /> when no
  ///   active row matches the given id.
  /// </summary>
  /// <param name="spendAccountId">The spend account primary key.</param>
  /// <param name="delta">The signed amount in whole VND to add to <c>Balance</c>.</param>
  Task ApplyDeltaAsync( long spendAccountId, long delta ) ;
}
