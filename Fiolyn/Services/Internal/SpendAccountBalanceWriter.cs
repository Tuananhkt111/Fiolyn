using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Default implementation of <see cref="ISpendAccountBalanceWriter" /> that issues an EF Core
///   <c>ExecuteUpdate</c> with `Balance = Balance + @delta` so two concurrent writers cannot lose an
///   update. Skips the call entirely when the delta is zero. Updates
///   <see cref="SpendAccount.LastModified" /> in the same statement since `SaveChanges` is not
///   invoked on this path.
/// </summary>
public class SpendAccountBalanceWriter(
  IRepository<SpendAccount> spendRepository ) : ISpendAccountBalanceWriter
{
  /// <inheritdoc />
  public async Task ApplyDeltaAsync( long spendAccountId, long delta )
  {
    if ( delta == 0L )
    {
      return ;
    }

    var rows = await spendRepository.QueryActive()
      .Where( s => s.Id == spendAccountId )
      .ExecuteUpdateAsync( setters => setters
        .SetProperty( s => s.Balance, s => s.Balance + delta )
        .SetProperty( s => s.LastModified, _ => DateTime.UtcNow ) ) ;

    if ( rows == 0 )
    {
      throw new KeyNotFoundException( $"SpendAccount {spendAccountId} not found." ) ;
    }
  }
}
