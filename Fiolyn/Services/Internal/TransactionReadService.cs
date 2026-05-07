using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="ITransactionReadService" />
public class TransactionReadService(
  IRepository<TransactionBase> transactionRepository ) : ITransactionReadService
{
  /// <inheritdoc />
  public async Task<List<TransactionBase>> GetAllForUserAsync( long userId ) =>
    await BuildQuery()
      .Where( t => t.SpendAccount.Members.Any( m => m.AppUserId == userId )
                   || ( t is TransferTransaction
                        && ( ( TransferTransaction )t ).DestinationSpendAccount.Members.Any( m =>
                          m.AppUserId == userId ) )
                   || ( t is StockTransaction
                        && ( ( StockTransaction )t ).BrokerAccount.Members.Any( m => m.AppUserId == userId ) ) )
      .ToListAsync() ;

  /// <inheritdoc />
  public async Task<List<TransactionBase>> GetAllAsync( long? userId = null )
  {
    var query = BuildQuery() ;
    if ( userId is not null )
    {
      query = query
        .Where( t => t.SpendAccount.Members.Any( m => m.AppUserId == userId.Value )
                     || ( t is TransferTransaction
                          && ( ( TransferTransaction )t ).DestinationSpendAccount.Members.Any( m =>
                            m.AppUserId == userId.Value ) )
                     || ( t is StockTransaction
                          && ( ( StockTransaction )t ).BrokerAccount.Members.Any( m =>
                            m.AppUserId == userId.Value ) ) ) ;
    }

    return await query.ToListAsync() ;
  }

  private IQueryable<TransactionBase> BuildQuery() =>
    transactionRepository.Query()
      .AsNoTracking()
      .Include( t => t.SpendAccount )
      .Include( t => t.Category )
      .Include( t => t.Payer )
      .Include( t => ( t as StockTransaction )!.BrokerAccount )
      .Include( t => ( t as TransferTransaction )!.DestinationSpendAccount )
      .Include( t => ( t as LendTransaction )!.Counterparty )
      .Include( t => ( t as BorrowingTransaction )!.Counterparty )
      .OrderByDescending( t => t.Date ) ;
}
