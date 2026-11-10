using Fiolyn.Common.Pagination ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal.Helpers ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="ITransferTransactionService" />
public class TransferTransactionService(
  IRepository<TransferTransaction> transferRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository ) : ITransferTransactionService
{
  /// <inheritdoc />
  public async Task<TransferTransaction> CreateAsync( TransferTransaction transaction, long userId )
  {
    EnsureSourceDifferentFromDestination( transaction.SpendAccountId, transaction.DestinationSpendAccountId ) ;
    await EnsureBothMembershipAsync(
      transaction.SpendAccountId, transaction.DestinationSpendAccountId, userId ) ;
    await TransactionCategoryGuard.EnsureKindMatchesAsync(
      categoryRepository, transaction.CategoryId, TransactionKind.Transfer ) ;

    var source = await spendRepository.GetByIdOrThrowAsync( transaction.SpendAccountId ) ;
    var destination = await spendRepository.GetByIdOrThrowAsync( transaction.DestinationSpendAccountId ) ;

    source.Balance -= transaction.Amount ;
    destination.Balance += transaction.Amount ;
    await transferRepository.AddAsync( transaction ) ;
    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(
      transferRepository, transaction.SpendAccountId, transaction.DestinationSpendAccountId ) ;

    return transaction ;
  }

  /// <inheritdoc />
  public async Task UpdateAsync( TransferTransaction transaction, long userId )
  {
    EnsureSourceDifferentFromDestination(
      transaction.SpendAccountId, transaction.DestinationSpendAccountId ) ;

    var fetched = await transferRepository.Query()
      .AsNoTracking()
      .Where( t => t.Id == transaction.Id )
      .Select( t => new
      {
        t.Amount,
        t.SpendAccountId,
        t.DestinationSpendAccountId
      } )
      .FirstOrDefaultAsync() ;

    if ( fetched is null )
    {
      throw new KeyNotFoundException( $"TransferTransaction {transaction.Id} not found." ) ;
    }

    // The old legs are reversed below, so the caller must belong to both accounts the transfer
    // touches today, not only to the ones it is being moved to.
    await EnsureBothMembershipAsync( fetched.SpendAccountId, fetched.DestinationSpendAccountId, userId ) ;
    await EnsureBothMembershipAsync(
      transaction.SpendAccountId, transaction.DestinationSpendAccountId, userId ) ;
    await TransactionCategoryGuard.EnsureKindMatchesAsync(
      categoryRepository, transaction.CategoryId, TransactionKind.Transfer ) ;

    var oldAmount = fetched.Amount ;
    var oldSourceId = fetched.SpendAccountId ;
    var oldDestId = fetched.DestinationSpendAccountId ;
    var newSourceId = transaction.SpendAccountId ;
    var newDestId = transaction.DestinationSpendAccountId ;
    var newAmount = transaction.Amount ;

    // Reverse the old legs, then apply the new ones. Touched accounts deduplicated so we don't
    // load and mutate the same SpendAccount twice in this DbContext.
    var deltas = new Dictionary<long, long>() ;
    deltas[ oldSourceId ] = deltas.GetValueOrDefault( oldSourceId, 0 ) + oldAmount ;
    deltas[ oldDestId ] = deltas.GetValueOrDefault( oldDestId, 0 ) - oldAmount ;
    deltas[ newSourceId ] = deltas.GetValueOrDefault( newSourceId, 0 ) - newAmount ;
    deltas[ newDestId ] = deltas.GetValueOrDefault( newDestId, 0 ) + newAmount ;

    foreach ( var (spendId, delta) in deltas )
    {
      if ( delta == 0L )
      {
        continue ;
      }

      var account = await spendRepository.GetByIdOrThrowAsync( spendId ) ;
      account.Balance += delta ;
    }

    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(
      transferRepository, deltas.Keys.ToArray() ) ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id, long userId )
  {
    // GetByIdForUserAsync admits a member of either side (reads show incoming transfers to the
    // destination's members). Deleting reverses both legs, so it needs membership of both.
    var transaction = await GetByIdForUserAsync( id, userId )
                      ?? throw new KeyNotFoundException(
                        $"TransferTransaction {id} not found for user {userId}." ) ;
    await EnsureBothMembershipAsync( transaction.SpendAccountId, transaction.DestinationSpendAccountId, userId ) ;
    await SoftDeleteAndReverseAsync( transaction ) ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var transaction = await GetByIdAsync( id )
                      ?? throw new KeyNotFoundException( $"TransferTransaction {id} not found." ) ;
    await SoftDeleteAndReverseAsync( transaction ) ;
  }

  /// <inheritdoc />
  public async Task<PagedResult<TransferTransaction>> GetAllForUserAsync(
    long userId, PaginationQuery pagination ) =>
    await BuildReadQuery( transferRepository.Query()
        .Where( t => t.SpendAccount.Members.Any( m => m.AppUserId == userId )
                     || t.DestinationSpendAccount.Members.Any( m => m.AppUserId == userId ) ) )
      .AsNoTracking()
      .ToPagedResultAsync( pagination ) ;

  /// <inheritdoc />
  public async Task<TransferTransaction?> GetByIdForUserAsync( long id, long userId ) =>
    await BuildReadQuery( transferRepository.Query()
        .Where( t => t.Id == id
                     && ( t.SpendAccount.Members.Any( m => m.AppUserId == userId )
                          || t.DestinationSpendAccount.Members.Any( m => m.AppUserId == userId ) ) ) )
      .FirstOrDefaultAsync() ;

  /// <inheritdoc />
  public async Task<bool> ExistsForUserAsync( long id, long userId ) =>
    await transferRepository.Query()
      .AnyAsync( t => t.Id == id
                      && ( t.SpendAccount.Members.Any( m => m.AppUserId == userId )
                           || t.DestinationSpendAccount.Members.Any( m => m.AppUserId == userId ) ) ) ;

  /// <inheritdoc />
  public async Task<PagedResult<TransferTransaction>> GetAllAsync(
    PaginationQuery pagination, long? userId = null )
  {
    var query = transferRepository.Query() ;
    if ( userId is not null )
    {
      query = query.Where( t =>
        t.SpendAccount.Members.Any( m => m.AppUserId == userId.Value )
        || t.DestinationSpendAccount.Members.Any( m => m.AppUserId == userId.Value ) ) ;
    }

    return await BuildReadQuery( query ).AsNoTracking().ToPagedResultAsync( pagination ) ;
  }

  /// <inheritdoc />
  public async Task<TransferTransaction?> GetByIdAsync( long id ) =>
    await BuildReadQuery( transferRepository.Query().Where( t => t.Id == id ) )
      .FirstOrDefaultAsync() ;

  private async Task SoftDeleteAndReverseAsync( TransferTransaction transaction )
  {
    var source = await spendRepository.GetByIdOrThrowAsync( transaction.SpendAccountId ) ;
    var destination = await spendRepository.GetByIdOrThrowAsync( transaction.DestinationSpendAccountId ) ;

    source.Balance += transaction.Amount ;
    destination.Balance -= transaction.Amount ;
    transferRepository.SoftDelete( transaction ) ;

    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(
      transferRepository, transaction.SpendAccountId, transaction.DestinationSpendAccountId ) ;
  }

  // Tracking-on by default — single-row callers (Update, SoftDelete) need the tracked entity and
  // its included SpendAccount / DestinationSpendAccount in the change tracker, so the subsequent
  // FindAsync calls inside the write paths return the same instances instead of allocating a
  // duplicate-keyed tracked entity. List callers chain .AsNoTracking() themselves before
  // .ToListAsync().
  private static IQueryable<TransferTransaction> BuildReadQuery( IQueryable<TransferTransaction> query ) =>
    query
      .Include( t => t.SpendAccount )
      .Include( t => t.DestinationSpendAccount )
      .Include( t => t.Category )
      .Include( t => t.Payer )
      .OrderByDescending( t => t.Date )
      .ThenByDescending( t => t.Id ) ;

  private static void EnsureSourceDifferentFromDestination( long sourceId, long destinationId )
  {
    if ( sourceId == destinationId )
    {
      throw new BadRequestException( "Source and destination spend accounts must differ." ) ;
    }
  }

  private async Task EnsureBothMembershipAsync( long sourceId, long destinationId, long userId )
  {
    // Single round-trip: confirm both accounts exist active AND caller is a member of both.
    var ok = await spendRepository.Query()
      .Where( s => s.Id == sourceId || s.Id == destinationId )
      .GroupBy( s => 1 )
      .Select( g => new
      {
        Count = g.Count(),
        MembershipCount = g.Count( s => s.Members.Any( m => m.AppUserId == userId ) )
      } )
      .FirstOrDefaultAsync() ;

    if ( ok is null || ok.Count != 2 || ok.MembershipCount != 2 )
    {
      throw new KeyNotFoundException(
        $"SpendAccount {sourceId} or {destinationId} not found for user {userId}." ) ;
    }
  }
}
