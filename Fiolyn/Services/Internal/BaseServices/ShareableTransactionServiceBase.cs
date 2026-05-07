using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal.Helpers ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal.BaseServices ;

/// <summary>
///   Shared write/read plumbing for transaction leaves whose request supports a
///   <c>shares: [{ counterpartyName, percentage }]</c> field — currently
///   <see cref="IncomeTransaction" /> and <see cref="ExpenseTransaction" />. Each share is
///   atomically materialised as a paired child row (<see cref="LendTransaction" /> for Expense
///   parents, <see cref="BorrowingTransaction" /> for Income parents) linked back via
///   <see cref="TransactionBase.ParentTransactionId" />, so the receivable / payable lives
///   directly in the existing Lend / Borrow ledger. <c>SpendAccount.Balance</c> still moves by
///   the full original amount: parent gets <c>amount - sum(childAmount)</c> and each child gets
///   its share's slice (parent absorbs rounding loss). PUT replaces children wholesale;
///   soft-delete cascades to them.
/// </summary>
public abstract class ShareableTransactionServiceBase<TParent, TChild>(
  IRepository<TParent> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository,
  IRepository<Counterparty> counterpartyRepository,
  IRepository<TChild> childRepository )
  : StandardTransactionServiceBase<TParent>( transactionRepository, spendRepository, categoryRepository )
  where TParent : TransactionBase
  where TChild : TransactionBase, IHasCounterparty, new()
{
  private const string SharedWithSuffixFormat = "Shared with {0}" ;
  private const string SharedWithDescriptionFormat = "Shared with {0}: {1}" ;
  private const int PercentDivisor = 100 ;

  public async Task<TParent> CreateAsync(
    TParent parent, long userId, IReadOnlyList<TransactionShareInput> shares )
  {
    if ( shares.Count == 0 )
    {
      return await CreateAsync( parent, userId ) ;
    }

    await EnsureMembershipAsync( parent.SpendAccountId, userId ) ;
    await TransactionCategoryGuard.EnsureKindMatchesAsync( CategoryRepository, parent.CategoryId, Kind ) ;

    var resolved = await ResolveSharesAsync( shares, userId ) ;

    var totalAmount = parent.Amount ;
    var children = SplitAndBuildChildren( parent, resolved, totalAmount ) ;

    await TransactionRepository.AddAsync( parent ) ;
    foreach ( var child in children )
    {
      await childRepository.AddAsync( child ) ;
    }

    var spendAccount = await SpendRepository.GetByIdOrThrowAsync( parent.SpendAccountId ) ;
    spendAccount.Balance += totalAmount ;

    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(
      TransactionRepository, parent.SpendAccountId ) ;

    return parent ;
  }

  public async Task UpdateAsync(
    TParent parent, long userId, IReadOnlyList<TransactionShareInput> shares )
  {
    var newSpendId = parent.SpendAccountId ;
    // Don't try to sum children in this projection — they live in a different leaf table than
    // TParent, so a subquery against TransactionRepository would always return 0. We pull
    // `oldChildrenSum` from the already-eager-loaded `parent.Children` collection below.
    var fetched = await TransactionRepository.Query()
      .AsNoTracking()
      .Where( t => t.Id == parent.Id )
      .Select( t => new
      {
        t.Amount,
        t.SpendAccountId,
        NewSpendAccountIsMember = SpendRepository.Query()
          .Any( s => s.Id == newSpendId
                     && s.Members.Any( m => m.AppUserId == userId ) )
      } )
      .FirstOrDefaultAsync() ;

    if ( fetched is null )
    {
      throw new KeyNotFoundException( $"Transaction {parent.Id} not found." ) ;
    }

    if ( ! fetched.NewSpendAccountIsMember )
    {
      throw new KeyNotFoundException(
        $"SpendAccount {newSpendId} not found for user {userId}." ) ;
    }

    await TransactionCategoryGuard.EnsureKindMatchesAsync( CategoryRepository, parent.CategoryId, Kind ) ;

    var resolved = await ResolveSharesAsync( shares, userId ) ;

    var newTotalAmount = parent.Amount ;
    var oldChildrenSum = parent.Children.Sum( c => c.Amount ) ;
    var oldTotalAmount = fetched.Amount + oldChildrenSum ;

    foreach ( var existing in parent.Children.ToList() )
    {
      childRepository.Delete( ( TChild )existing ) ;
    }

    parent.Children.Clear() ;

    var newChildren = SplitAndBuildChildren( parent, resolved, newTotalAmount ) ;
    foreach ( var child in newChildren )
    {
      await childRepository.AddAsync( child ) ;
    }

    var oldSpendId = fetched.SpendAccountId ;
    if ( oldSpendId == newSpendId )
    {
      var net = newTotalAmount - oldTotalAmount ;
      if ( net != 0L )
      {
        var spendAccount = await SpendRepository.GetByIdOrThrowAsync( oldSpendId ) ;
        spendAccount.Balance += net ;
      }
    }
    else
    {
      var oldSpend = await SpendRepository.GetByIdOrThrowAsync( oldSpendId ) ;
      oldSpend.Balance -= oldTotalAmount ;
      var newSpend = await SpendRepository.GetByIdOrThrowAsync( newSpendId ) ;
      newSpend.Balance += newTotalAmount ;
    }

    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(
      TransactionRepository, oldSpendId, newSpendId ) ;
  }

  /// <inheritdoc />
  protected override async Task SoftDeleteAndReverseAsync( TParent entity )
  {
    var totalToReverse = entity.Amount + entity.Children.Sum( c => c.Amount ) ;

    var spendAccount = await SpendRepository.GetByIdOrThrowAsync( entity.SpendAccountId ) ;
    spendAccount.Balance -= totalToReverse ;

    TransactionRepository.SoftDelete( entity ) ;
    foreach ( var child in entity.Children.ToList() )
    {
      childRepository.SoftDelete( ( TChild )child ) ;
    }

    await TransactionBalanceWriter.SaveWithConcurrencyGuardAsync(
      TransactionRepository, entity.SpendAccountId ) ;
  }

  /// <inheritdoc />
  protected override IQueryable<TParent> BuildReadQuery( IQueryable<TParent> query ) =>
    base.BuildReadQuery( query )
      // EF Core's translator only reliably resolves cast-includes against concrete TPT
      // subtypes — interface casts on ThenInclude (e.g. `c as IHasCounterparty`) are not
      // guaranteed. Income parents only ever produce BorrowingTransaction children; Expense
      // parents only ever produce LendTransaction children; chain both ThenIncludes so the
      // shared base works for either leaf without per-leaf overrides.
      .Include( t => t.Children ).ThenInclude( c => ( c as LendTransaction )!.Counterparty )
      .Include( t => t.Children ).ThenInclude( c => ( c as BorrowingTransaction )!.Counterparty ) ;

  private List<TChild> SplitAndBuildChildren(
    TParent parent, IReadOnlyList<(Counterparty Counterparty, int Percentage)> resolved, long totalAmount )
  {
    var sign = Math.Sign( totalAmount ) ;
    var magnitude = Math.Abs( totalAmount ) ;

    var children = new List<TChild>( resolved.Count ) ;
    var childSum = 0L ;
    foreach ( var (counterparty, percentage) in resolved )
    {
      var childAmount = sign * ( magnitude * percentage / PercentDivisor ) ;
      childSum += childAmount ;
      var child = new TChild
      {
        SpendAccountId = parent.SpendAccountId,
        Date = parent.Date,
        PayerId = parent.PayerId,
        Description = string.IsNullOrWhiteSpace( parent.Description )
          ? string.Format( SharedWithSuffixFormat, counterparty.Name )
          : string.Format( SharedWithDescriptionFormat, counterparty.Name, parent.Description ),
        ParentTransaction = parent,
        Counterparty = counterparty,
        Amount = childAmount
      } ;
      children.Add( child ) ;
      parent.Children.Add( child ) ;
    }

    // Parent absorbs rounding loss so parent + sum(children) == totalAmount exactly.
    parent.Amount = totalAmount - childSum ;
    return children ;
  }

  private async Task<List<(Counterparty Counterparty, int Percentage)>> ResolveSharesAsync(
    IReadOnlyList<TransactionShareInput> shares, long userId )
  {
    var trimmedNames = ValidateAndTrim( shares ) ;
    var counterparties = await CounterpartyResolver.ResolveOrCreateManyAsync(
      counterpartyRepository, trimmedNames, userId ) ;

    var resolved = new List<(Counterparty Counterparty, int Percentage)>( shares.Count ) ;
    var seenIds = new HashSet<long>() ;
    var seenStagedNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) ;

    for ( var i = 0 ; i < shares.Count ; i++ )
    {
      var counterparty = counterparties[ trimmedNames[ i ] ] ;
      // Persisted row → dedupe by Id; freshly-staged row (Id == 0) → dedupe by trimmed name
      // to catch "Bob" + "BOB" within one request before EF tries two inserts.
      var isDuplicate = counterparty.Id != 0
        ? ! seenIds.Add( counterparty.Id )
        : ! seenStagedNames.Add( counterparty.Name ) ;
      if ( isDuplicate )
      {
        throw new BadRequestException(
          $"Counterparty '{counterparty.Name}' appears more than once in the share list." ) ;
      }

      resolved.Add( ( counterparty, shares[ i ].Percentage ) ) ;
    }

    return resolved ;
  }

  private static string[] ValidateAndTrim( IReadOnlyList<TransactionShareInput> shares )
  {
    var totalPercentage = 0 ;
    var trimmedNames = new string[ shares.Count ] ;
    for ( var i = 0 ; i < shares.Count ; i++ )
    {
      var share = shares[ i ] ;
      if ( share.Percentage is < 1 or > PercentDivisor )
      {
        throw new BadRequestException(
          $"Share percentage {share.Percentage} is out of range (1-{PercentDivisor})." ) ;
      }

      if ( string.IsNullOrWhiteSpace( share.CounterpartyName ) )
      {
        throw new BadRequestException( "Counterparty name is required." ) ;
      }

      trimmedNames[ i ] = share.CounterpartyName.Trim() ;
      totalPercentage += share.Percentage ;
    }

    if ( totalPercentage > PercentDivisor )
    {
      throw new BadRequestException(
        $"Share percentages sum to {totalPercentage}, which exceeds {PercentDivisor}." ) ;
    }

    return trimmedNames ;
  }
}
