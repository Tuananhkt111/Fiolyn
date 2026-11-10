using Fiolyn.Common.Pagination ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Manages shared spend accounts. Access is controlled by membership rows in
///   <see cref="SpendAccountMember" /> — user-scoped reads/writes require the caller to be a member,
///   and "not a member" is surfaced as 404 (via <see cref="KeyNotFoundException" />).
/// </summary>
public class SpendAccountService(
  IRepository<SpendAccount> repository,
  IRepository<SpendAccountMember> memberRepository,
  IRepository<AppUser> userRepository,
  IRepository<BrokerAccount> brokerRepository,
  IRepository<TransactionBase> transactionRepository,
  IRepository<TransferTransaction> transferRepository ) : ISpendAccountService
{
  /// <inheritdoc />
  public async Task<SpendAccount> CreateAsync( SpendAccount account, long creatorUserId )
  {
    account.Members.Add( new SpendAccountMember
    {
      AppUserId = creatorUserId,
      JoinedAt = DateTime.UtcNow
    } ) ;
    await repository.AddAsync( account ) ;
    await repository.SaveAsync() ;
    return account ;
  }

  /// <inheritdoc />
  public async Task<PagedResult<SpendAccount>> GetAllForUserAsync( long userId, PaginationQuery pagination ) =>
    await repository.Query()
      .AsNoTracking()
      .Where( a => a.Members.Any( m => m.AppUserId == userId ) )
      .OrderBy( a => a.Name )
      .ThenBy( a => a.Id )
      .ToPagedResultAsync( pagination ) ;

  /// <inheritdoc />
  public async Task<SpendAccount?> GetByIdForUserAsync( long id, long userId ) =>
    await repository.Query()
      .FirstOrDefaultAsync( a => a.Id == id
                                 && a.Members.Any( m => m.AppUserId == userId ) ) ;

  /// <inheritdoc />
  public async Task<SpendAccount?> UpdateAsync( long id, long userId, string name, SpendAccountType type )
  {
    var account = await GetByIdForUserAsync( id, userId ) ;
    if ( account is null )
    {
      return null ;
    }

    account.Name = name ;
    account.Type = type ;
    await repository.SaveAsync() ;
    return account ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id, long userId )
  {
    var account = await GetByIdForUserAsync( id, userId )
                  ?? throw new KeyNotFoundException( $"SpendAccount {id} not found for user {userId}." ) ;
    await EnsureNotReferencedByActiveBrokerAsync( id ) ;
    await EnsureNotReferencedByActiveTransactionAsync( id ) ;
    repository.SoftDelete( account ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<PagedResult<SpendAccountMember>> GetMembersAsync(
    long accountId, long callerUserId, PaginationQuery pagination )
  {
    // One query: the caller-membership check sits in the same query that fetches the members
    // (docs/rules/access-control.md). Starting from the account keeps its soft-delete filter.
    // An account always has at least one member, so an empty result means "not a member" → 404.
    var page = await repository.Query()
      .Where( a => a.Id == accountId && a.Members.Any( m => m.AppUserId == callerUserId ) )
      .SelectMany( a => a.Members )
      .AsNoTracking()
      .Include( m => m.AppUser )
      .OrderBy( m => m.JoinedAt )
      .ThenBy( m => m.AppUserId )
      .ToPagedResultAsync( pagination ) ;

    if ( page.TotalCount == 0 )
    {
      throw new KeyNotFoundException( $"SpendAccount {accountId} not found for user {callerUserId}." ) ;
    }

    return page ;
  }

  /// <inheritdoc />
  public async Task<List<SpendAccountMember>> AddMemberAsync( long accountId, long callerUserId, string email )
  {
    var normalized = AppUser.NormalizeEmail( email ) ;

    var data = await repository.Query()
      .Where( a => a.Id == accountId
                   && a.Members.Any( m => m.AppUserId == callerUserId ) )
      .Select( a => a.Members
        .OrderBy( m => m.JoinedAt )
        .ToList() )
      .FirstOrDefaultAsync() ;

    if ( data is null )
    {
      throw new KeyNotFoundException( $"SpendAccount {accountId} not found for user {callerUserId}." ) ;
    }

    var targetId = await userRepository.Query()
                     .Where( u => u.Email == normalized )
                     .Select( u => ( long? )u.Id )
                     .FirstOrDefaultAsync()
                   ?? throw new KeyNotFoundException( $"User with email {normalized} not found." ) ;

    if ( data.Any( m => m.AppUserId == targetId ) )
    {
      return data ;
    }

    await memberRepository.AddAsync( new SpendAccountMember
    {
      SpendAccountId = accountId,
      AppUserId = targetId,
      JoinedAt = DateTime.UtcNow
    } ) ;
    await memberRepository.SaveAsync() ;

    return await LoadMembersAsync( accountId ) ;
  }

  /// <inheritdoc />
  public async Task RemoveMemberAsync( long accountId, long callerUserId, long targetUserId )
  {
    var members = await repository.Query()
      .Where( a => a.Id == accountId )
      .Select( a => a.Members.ToList() )
      .FirstOrDefaultAsync() ;

    if ( members is null || members.All( m => m.AppUserId != callerUserId ) )
    {
      throw new KeyNotFoundException( $"SpendAccount {accountId} not found for user {callerUserId}." ) ;
    }

    var target = members.FirstOrDefault( m => m.AppUserId == targetUserId )
                 ?? throw new KeyNotFoundException(
                   $"User {targetUserId} is not a member of SpendAccount {accountId}." ) ;

    if ( members.Count <= 1 )
    {
      throw new BadRequestException(
        "Cannot remove the last member — delete the account instead." ) ;
    }

    memberRepository.Delete( target ) ;
    await memberRepository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<PagedResult<SpendAccount>> GetAllAsync( PaginationQuery pagination, long? userId = null )
  {
    IQueryable<SpendAccount> query = repository.Query()
      .AsNoTracking()
      .Include( a => a.Members )
      .ThenInclude( m => m.AppUser ) ;

    if ( userId is not null )
    {
      query = query.Where( a => a.Members.Any( m => m.AppUserId == userId.Value ) ) ;
    }

    return await query
      .OrderBy( a => a.Name )
      .ThenBy( a => a.Id )
      .ToPagedResultAsync( pagination ) ;
  }

  /// <inheritdoc />
  public async Task<SpendAccount?> GetByIdAsync( long id ) =>
    await repository.Query()
      .Include( a => a.Members )
      .ThenInclude( m => m.AppUser )
      .FirstOrDefaultAsync( a => a.Id == id ) ;

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var account = await repository.GetByIdOrThrowAsync( id ) ;
    await EnsureNotReferencedByActiveBrokerAsync( id ) ;
    await EnsureNotReferencedByActiveTransactionAsync( id ) ;
    repository.SoftDelete( account ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<SpendAccount?> UpdateAsync( long id, string name, SpendAccountType type )
  {
    var account = await GetByIdAsync( id ) ;
    if ( account is null )
    {
      return null ;
    }

    account.Name = name ;
    account.Type = type ;
    await repository.SaveAsync() ;
    return account ;
  }

  private async Task EnsureNotReferencedByActiveBrokerAsync( long spendAccountId )
  {
    var stillReferenced = await brokerRepository.Query()
      .AnyAsync( b => b.SpendAccountId == spendAccountId ) ;
    if ( stillReferenced )
    {
      throw new BadRequestException(
        $"SpendAccount {spendAccountId} is linked from one or more active broker accounts — unlink them first." ) ;
    }
  }

  private async Task EnsureNotReferencedByActiveTransactionAsync( long spendAccountId )
  {
    var referencedAsSource = await transactionRepository.Query()
      .AnyAsync( t => t.SpendAccountId == spendAccountId ) ;
    if ( referencedAsSource )
    {
      throw new BadRequestException(
        $"SpendAccount {spendAccountId} is referenced by one or more active transactions — delete or reassign them first." ) ;
    }

    var referencedAsTransferDestination = await transferRepository.Query()
      .AnyAsync( t => t.DestinationSpendAccountId == spendAccountId ) ;
    if ( referencedAsTransferDestination )
    {
      throw new BadRequestException(
        $"SpendAccount {spendAccountId} is referenced by one or more active transfer transactions — delete or reassign them first." ) ;
    }
  }

  private async Task<List<SpendAccountMember>> LoadMembersAsync( long accountId ) =>
    await memberRepository.Query()
      .AsNoTracking()
      .Include( m => m.AppUser )
      .Where( m => m.SpendAccountId == accountId )
      .OrderBy( m => m.JoinedAt )
      .ToListAsync() ;
}
