using Fiolyn.Common.Pagination ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Manages shared broker accounts. Access is controlled by membership rows in
///   <see cref="BrokerAccountMember" /> — user-scoped reads/writes require the caller to be a member,
///   and "not a member" is surfaced as 404 (via <see cref="KeyNotFoundException" />).
///   Each broker references exactly one <see cref="SpendAccount" /> (auto-balance link); setting or
///   changing that link requires the caller to be a member of the target spend account too.
/// </summary>
public class BrokerAccountService(
  IRepository<BrokerAccount> repository,
  IRepository<BrokerAccountMember> memberRepository,
  IRepository<AppUser> userRepository,
  IRepository<SpendAccount> spendRepository ) : IBrokerAccountService
{
  /// <inheritdoc />
  public async Task<BrokerAccount> CreateAsync( BrokerAccount account, long creatorUserId )
  {
    await EnsureUserIsSpendAccountMemberAsync( account.SpendAccountId, creatorUserId ) ;
    account.Members.Add( new BrokerAccountMember
    {
      AppUserId = creatorUserId,
      JoinedAt = DateTime.UtcNow
    } ) ;
    await repository.AddAsync( account ) ;
    await repository.SaveAsync() ;
    return account ;
  }

  /// <inheritdoc />
  public async Task<PagedResult<BrokerAccount>> GetAllForUserAsync( long userId, PaginationQuery pagination ) =>
    await repository.Query()
      .AsNoTracking()
      .Include( b => b.SpendAccount )
      .Where( b => b.Members.Any( m => m.AppUserId == userId ) )
      .OrderBy( b => b.Name )
      .ThenBy( b => b.Id )
      .ToPagedResultAsync( pagination ) ;

  /// <inheritdoc />
  public async Task<BrokerAccount?> GetByIdForUserAsync( long id, long userId ) =>
    await repository.Query()
      .Include( b => b.SpendAccount )
      .FirstOrDefaultAsync( b => b.Id == id
                                 && b.Members.Any( m => m.AppUserId == userId ) ) ;

  /// <inheritdoc />
  public async Task<bool> ExistsForUserAsync( long id, long userId ) =>
    await repository.Query()
      .AnyAsync( b => b.Id == id
                      && b.Members.Any( m => m.AppUserId == userId ) ) ;

  /// <inheritdoc />
  public async Task<BrokerAccount?> UpdateAsync( long id, long userId, string name, long spendAccountId )
  {
    var account = await GetByIdForUserAsync( id, userId ) ;
    if ( account is null )
    {
      return null ;
    }

    var oldSpendId = account.SpendAccountId ;
    if ( oldSpendId != spendAccountId )
    {
      await EnsureUserIsSpendAccountMemberAsync( spendAccountId, userId ) ;
    }

    account.Name = name ;
    account.SpendAccountId = spendAccountId ;
    await repository.SaveAsync() ;

    if ( oldSpendId == spendAccountId )
    {
      return account ;
    }

    return await GetByIdForUserAsync( id, userId ) ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id, long userId )
  {
    var account = await GetByIdForUserAsync( id, userId )
                  ?? throw new KeyNotFoundException( $"BrokerAccount {id} not found for user {userId}." ) ;
    repository.SoftDelete( account ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<PagedResult<BrokerAccountMember>> GetMembersAsync(
    long accountId, long callerUserId, PaginationQuery pagination )
  {
    var callerIsMember = await repository.Query()
      .AnyAsync( b => b.Id == accountId
                      && b.Members.Any( m => m.AppUserId == callerUserId ) ) ;
    if ( ! callerIsMember )
    {
      throw new KeyNotFoundException( $"BrokerAccount {accountId} not found for user {callerUserId}." ) ;
    }

    return await memberRepository.Query()
      .AsNoTracking()
      .Include( m => m.AppUser )
      .Where( m => m.BrokerAccountId == accountId )
      .OrderBy( m => m.JoinedAt )
      .ThenBy( m => m.AppUserId )
      .ToPagedResultAsync( pagination ) ;
  }

  /// <inheritdoc />
  public async Task<List<BrokerAccountMember>> AddMemberAsync( long accountId, long callerUserId, string email )
  {
    var normalized = AppUser.NormalizeEmail( email ) ;

    var data = await repository.Query()
      .Where( b => b.Id == accountId
                   && b.Members.Any( m => m.AppUserId == callerUserId ) )
      .Select( b => b.Members
        .OrderBy( m => m.JoinedAt )
        .ToList() )
      .FirstOrDefaultAsync() ;

    if ( data is null )
    {
      throw new KeyNotFoundException( $"BrokerAccount {accountId} not found for user {callerUserId}." ) ;
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

    await memberRepository.AddAsync( new BrokerAccountMember
    {
      BrokerAccountId = accountId,
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
      .Where( b => b.Id == accountId )
      .Select( b => b.Members.ToList() )
      .FirstOrDefaultAsync() ;

    if ( members is null || members.All( m => m.AppUserId != callerUserId ) )
    {
      throw new KeyNotFoundException( $"BrokerAccount {accountId} not found for user {callerUserId}." ) ;
    }

    var target = members.FirstOrDefault( m => m.AppUserId == targetUserId )
                 ?? throw new KeyNotFoundException(
                   $"User {targetUserId} is not a member of BrokerAccount {accountId}." ) ;

    if ( members.Count <= 1 )
    {
      throw new BadRequestException(
        "Cannot remove the last member — delete the account instead." ) ;
    }

    memberRepository.Delete( target ) ;
    await memberRepository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<PagedResult<BrokerAccount>> GetAllAsync( PaginationQuery pagination, long? userId = null )
  {
    IQueryable<BrokerAccount> query = repository.Query()
      .AsNoTracking()
      .Include( b => b.SpendAccount )
      .Include( b => b.Members )
      .ThenInclude( m => m.AppUser ) ;

    if ( userId is not null )
    {
      query = query.Where( b => b.Members.Any( m => m.AppUserId == userId.Value ) ) ;
    }

    return await query
      .OrderBy( b => b.Name )
      .ThenBy( b => b.Id )
      .ToPagedResultAsync( pagination ) ;
  }

  /// <inheritdoc />
  public async Task<BrokerAccount?> GetByIdAsync( long id ) =>
    await repository.Query()
      .Include( b => b.SpendAccount )
      .Include( b => b.Members )
      .ThenInclude( m => m.AppUser )
      .FirstOrDefaultAsync( b => b.Id == id ) ;

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var account = await repository.GetByIdOrThrowAsync( id ) ;
    repository.SoftDelete( account ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<BrokerAccount?> UpdateAsync( long id, string name, long spendAccountId )
  {
    var account = await GetByIdAsync( id ) ;
    if ( account is null )
    {
      return null ;
    }

    var oldSpendId = account.SpendAccountId ;
    if ( oldSpendId != spendAccountId )
    {
      await spendRepository.GetByIdOrThrowAsync( spendAccountId ) ;
    }

    account.Name = name ;
    account.SpendAccountId = spendAccountId ;
    await repository.SaveAsync() ;

    if ( oldSpendId == spendAccountId )
    {
      return account ;
    }

    return await GetByIdAsync( id ) ;
  }

  private async Task EnsureUserIsSpendAccountMemberAsync( long spendAccountId, long userId )
  {
    var ok = await spendRepository.Query()
      .AnyAsync( s => s.Id == spendAccountId
                      && s.Members.Any( m => m.AppUserId == userId ) ) ;

    if ( ! ok )
    {
      throw new KeyNotFoundException( $"SpendAccount {spendAccountId} not found for user {userId}." ) ;
    }
  }

  private async Task<List<BrokerAccountMember>> LoadMembersAsync( long accountId ) =>
    await memberRepository.Query()
      .AsNoTracking()
      .Include( m => m.AppUser )
      .Where( m => m.BrokerAccountId == accountId )
      .OrderBy( m => m.JoinedAt )
      .ToListAsync() ;
}
