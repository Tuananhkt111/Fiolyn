using Fiolyn.Controllers.DTOs.Responses.Obligation ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="IObligationService" />
public class ObligationService(
  IRepository<Counterparty> counterpartyRepository,
  IRepository<LendTransaction> lendRepository,
  IRepository<BorrowingTransaction> borrowingRepository ) : IObligationService
{
  /// <inheritdoc />
  public async Task<List<ObligationResponse>> GetAllForUserAsync( long userId )
  {
    // Lend rows store negative Amount (money out, recoverable) — receivable is the unsigned
    // magnitude. Auto-created share children (ParentTransactionId set) live in this same
    // table, so they're already counted.
    var lendByCounterparty = await lendRepository.Query()
      .Where( t => t.Counterparty.AppUserId == userId )
      .GroupBy( t => t.CounterpartyId )
      .Select( g => new
      {
        CounterpartyId = g.Key,
        Total = g.Sum( t => Math.Abs( t.Amount ) )
      } )
      .ToListAsync() ;

    // Borrowing rows store positive Amount — payable is the unsigned magnitude.
    var borrowingByCounterparty = await borrowingRepository.Query()
      .Where( t => t.Counterparty.AppUserId == userId )
      .GroupBy( t => t.CounterpartyId )
      .Select( g => new
      {
        CounterpartyId = g.Key,
        Total = g.Sum( t => Math.Abs( t.Amount ) )
      } )
      .ToListAsync() ;

    var counterparties = await counterpartyRepository.Query()
      .Where( c => c.AppUserId == userId )
      .OrderBy( c => c.Name )
      .Select( c => new
      {
        c.Id,
        c.Name
      } )
      .ToListAsync() ;

    var lendMap = lendByCounterparty.ToDictionary( x => x.CounterpartyId, x => x.Total ) ;
    var borrowingMap = borrowingByCounterparty.ToDictionary( x => x.CounterpartyId, x => x.Total ) ;

    var result = new List<ObligationResponse>( counterparties.Count ) ;
    foreach ( var c in counterparties )
    {
      var receivable = lendMap.GetValueOrDefault( c.Id ) ;
      var payable = borrowingMap.GetValueOrDefault( c.Id ) ;
      if ( receivable == 0L && payable == 0L )
      {
        continue ;
      }

      result.Add( new ObligationResponse( c.Id, c.Name, receivable, payable, receivable - payable ) ) ;
    }

    return result ;
  }
}
