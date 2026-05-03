using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal.Helpers ;

/// <summary>
///   Shared CRUD + balance plumbing for the single-spend-account transaction leaves
///   (Income, Expense, Lend, Borrowing, Adjustment). User-scoped methods enforce
///   <see cref="SpendAccount" /> membership; admin variants skip the check. The signed
///   <c>Amount</c> is set by the caller before reaching this layer; on Update, the net delta is
///   applied to the spend account in the same atomic save as the row write.
///   <see cref="TransferTransaction" /> needs a destination spend account too and lives in its own
///   service.
/// </summary>
public abstract class StandardTransactionServiceBase<T>(
  IRepository<T> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository ) where T : TransactionBase
{
  /// <summary>
  ///   The kind of transaction this service handles. Used to validate that any
  ///   <c>CategoryId</c> the caller supplies has a matching <c>TransactionCategory.Kind</c>.
  /// </summary>
  protected abstract TransactionKind Kind { get ; }

  /// <summary>
  ///   Verifies caller is a member of the spend account, applies <c>entity.Amount</c> to the
  ///   balance, inserts the row, and saves once. Soft-deleted spend accounts return 404 via the
  ///   global query filter.
  /// </summary>
  public async Task<T> CreateAsync( T entity, long userId )
  {
    await EnsureMembershipAsync( entity.SpendAccountId, userId ) ;
    await TransactionCategoryGuard.EnsureKindMatchesAsync( categoryRepository, entity.CategoryId, Kind ) ;

    var spendAccount = await spendRepository.GetByIdOrThrowAsync( entity.SpendAccountId ) ;
    spendAccount.Balance += entity.Amount ;
    await transactionRepository.AddAsync( entity ) ;
    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(
      transactionRepository, entity.SpendAccountId ) ;
    return entity ;
  }

  /// <summary>
  ///   Same shape as <see cref="StockTransactionService.UpdateAsync" /> — projects the original
  ///   amount + spend id + new-spend membership, validates the kind match, then either applies a
  ///   net delta (same-spend) or reverses old / applies new (cross-spend). The mutated entity is
  ///   passed in tracked from a prior <see cref="GetByIdForUserAsync" /> so EF detects the changes
  ///   without an explicit <c>Update</c>.
  /// </summary>
  public async Task UpdateAsync( T entity, long userId )
  {
    var newSpendId = entity.SpendAccountId ;
    var fetched = await transactionRepository.Query()
      .AsNoTracking()
      .Where( t => t.Id == entity.Id )
      .Select( t => new
      {
        t.Amount,
        t.SpendAccountId,
        NewSpendAccountIsMember = spendRepository.Query()
          .Any( s => s.Id == newSpendId
                     && s.Members.Any( m => m.AppUserId == userId ) )
      } )
      .FirstOrDefaultAsync() ;

    if ( fetched is null )
    {
      throw new KeyNotFoundException( $"Transaction {entity.Id} not found." ) ;
    }

    if ( ! fetched.NewSpendAccountIsMember )
    {
      throw new KeyNotFoundException(
        $"SpendAccount {newSpendId} not found for user {userId}." ) ;
    }

    await TransactionCategoryGuard.EnsureKindMatchesAsync( categoryRepository, entity.CategoryId, Kind ) ;

    var oldDelta = fetched.Amount ;
    var newDelta = entity.Amount ;
    var oldSpendId = fetched.SpendAccountId ;

    if ( oldSpendId == newSpendId )
    {
      var net = newDelta - oldDelta ;
      if ( net != 0L )
      {
        var spendAccount = await spendRepository.GetByIdOrThrowAsync( oldSpendId ) ;
        spendAccount.Balance += net ;
      }
    }
    else
    {
      var oldSpend = await spendRepository.GetByIdOrThrowAsync( oldSpendId ) ;
      oldSpend.Balance -= oldDelta ;
      var newSpend = await spendRepository.GetByIdOrThrowAsync( newSpendId ) ;
      newSpend.Balance += newDelta ;
    }

    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(
      transactionRepository, oldSpendId, newSpendId ) ;
  }

  /// <summary>
  ///   Reverses the original <c>Amount</c> on the spend account balance, soft-deletes the row, and
  ///   saves once.
  /// </summary>
  public async Task SoftDeleteAsync( long id, long userId )
  {
    var entity = await GetByIdForUserAsync( id, userId )
                 ?? throw new KeyNotFoundException(
                   $"Transaction {id} not found for user {userId}." ) ;
    await SoftDeleteAndReverseAsync( entity ) ;
  }

  /// <inheritdoc cref="SoftDeleteAsync(long, long)" />
  public async Task SoftDeleteAsync( long id )
  {
    var entity = await GetByIdAsync( id )
                 ?? throw new KeyNotFoundException( $"Transaction {id} not found." ) ;
    await SoftDeleteAndReverseAsync( entity ) ;
  }

  /// <summary>
  ///   Lists rows the caller has access to via spend-account membership. Ordered by <c>Date</c>
  ///   descending. Eager-loads <c>SpendAccount</c>, <c>Category</c>, <c>Payer</c> for response
  ///   mapping.
  /// </summary>
  public async Task<List<T>> GetAllForUserAsync( long userId ) =>
    await BuildReadQuery( transactionRepository.Query()
        .Where( t => t.SpendAccount.Members.Any( m => m.AppUserId == userId ) ) )
      .ToListAsync() ;

  /// <summary>
  ///   Single-row read scoped to caller membership. Returns null if missing or not a member.
  /// </summary>
  public async Task<T?> GetByIdForUserAsync( long id, long userId ) =>
    await BuildReadQuery( transactionRepository.Query()
        .Where( t => t.Id == id && t.SpendAccount.Members.Any( m => m.AppUserId == userId ) ) )
      .FirstOrDefaultAsync() ;

  /// <summary>
  ///   Cheap existence check used by HEAD endpoints.
  /// </summary>
  public async Task<bool> ExistsForUserAsync( long id, long userId ) =>
    await transactionRepository.Query()
      .AnyAsync( t => t.Id == id && t.SpendAccount.Members.Any( m => m.AppUserId == userId ) ) ;

  /// <summary>
  ///   (Admin) Lists rows without user-scoping. Optionally filter to a single user's
  ///   spend-account memberships.
  /// </summary>
  public async Task<List<T>> GetAllAsync( long? userId = null )
  {
    var query = transactionRepository.Query() ;
    if ( userId is not null )
    {
      query = query.Where( t => t.SpendAccount.Members.Any( m => m.AppUserId == userId.Value ) ) ;
    }

    return await BuildReadQuery( query ).ToListAsync() ;
  }

  /// <summary>
  ///   (Admin) Single-row read without user-scoping.
  /// </summary>
  public async Task<T?> GetByIdAsync( long id ) =>
    await BuildReadQuery( transactionRepository.Query().Where( t => t.Id == id ) )
      .FirstOrDefaultAsync() ;

  /// <summary>
  ///   Hook for derived services to extend the read query with type-specific includes (e.g.
  ///   <c>TransferTransaction.DestinationSpendAccount</c>). Default implementation eager-loads the
  ///   columns every leaf needs.
  /// </summary>
  protected virtual IQueryable<T> BuildReadQuery( IQueryable<T> query ) =>
    query.AsNoTracking()
      .Include( t => t.SpendAccount )
      .Include( t => t.Category )
      .Include( t => t.Payer )
      .OrderByDescending( t => t.Date ) ;

  private async Task SoftDeleteAndReverseAsync( T entity )
  {
    var spendAccount = await spendRepository.GetByIdOrThrowAsync( entity.SpendAccountId ) ;
    spendAccount.Balance -= entity.Amount ;
    transactionRepository.SoftDelete( entity ) ;
    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(
      transactionRepository, entity.SpendAccountId ) ;
  }

  private async Task EnsureMembershipAsync( long spendAccountId, long userId )
  {
    var ok = await spendRepository.Query()
      .AnyAsync( s => s.Id == spendAccountId
                      && s.Members.Any( m => m.AppUserId == userId ) ) ;
    if ( ! ok )
    {
      throw new KeyNotFoundException(
        $"SpendAccount {spendAccountId} not found for user {userId}." ) ;
    }
  }
}
