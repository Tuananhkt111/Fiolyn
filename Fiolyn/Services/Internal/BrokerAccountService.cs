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
  public async Task<List<BrokerAccount>> GetAllForUserAsync( long userId ) =>
    await repository.QueryActive()
      .AsNoTracking()
      .Include( b => b.SpendAccount )
      .Where( b => b.Members.Any( m => m.AppUserId == userId && ! m.AppUser.IsDeleted ) )
      .OrderBy( b => b.Name )
      .ToListAsync() ;

  /// <inheritdoc />
  public async Task<BrokerAccount?> GetByIdForUserAsync( long id, long userId ) =>
    await repository.QueryActive()
      .Include( b => b.SpendAccount )
      .FirstOrDefaultAsync( b => b.Id == id
                                 && b.Members.Any( m => m.AppUserId == userId && ! m.AppUser.IsDeleted ) ) ;

  /// <inheritdoc />
  public async Task<bool> ExistsForUserAsync( long id, long userId ) =>
    await repository.QueryActive()
      .AnyAsync( b => b.Id == id
                      && b.Members.Any( m => m.AppUserId == userId && ! m.AppUser.IsDeleted ) ) ;

  /// <inheritdoc />
  public async Task UpdateAsync( BrokerAccount account, long callerUserId )
  {
    var oldSpendId = await repository.Query()
      .AsNoTracking()
      .Where( b => b.Id == account.Id )
      .Select( b => ( long? )b.SpendAccountId )
      .FirstOrDefaultAsync() ;

    if ( oldSpendId != account.SpendAccountId )
    {
      await EnsureUserIsSpendAccountMemberAsync( account.SpendAccountId, callerUserId ) ;
    }

    repository.Update( account ) ;
    await repository.SaveAsync() ;
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
  public async Task<List<BrokerAccountMember>> GetMembersAsync( long accountId, long callerUserId )
  {
    await EnsureCallerIsMemberAsync( accountId, callerUserId ) ;
    return await LoadActiveMembersAsync( accountId ) ;
  }

  /// <inheritdoc />
  public async Task<List<BrokerAccountMember>> AddMemberAsync( long accountId, long callerUserId, string email )
  {
    await EnsureCallerIsMemberAsync( accountId, callerUserId ) ;

    var normalized = AppUser.NormalizeEmail( email ) ;
    var target = await userRepository.QueryActive()
                   .FirstOrDefaultAsync( u => u.Email == normalized )
                 ?? throw new KeyNotFoundException( $"User with email {normalized} not found." ) ;

    var alreadyMember = await memberRepository.Query()
      .AnyAsync( m => m.BrokerAccountId == accountId && m.AppUserId == target.Id ) ;

    if ( ! alreadyMember )
    {
      await memberRepository.AddAsync( new BrokerAccountMember
      {
        BrokerAccountId = accountId,
        AppUserId = target.Id,
        JoinedAt = DateTime.UtcNow
      } ) ;
      await memberRepository.SaveAsync() ;
    }

    return await LoadActiveMembersAsync( accountId ) ;
  }

  /// <inheritdoc />
  public async Task RemoveMemberAsync( long accountId, long callerUserId, long targetUserId )
  {
    await EnsureCallerIsMemberAsync( accountId, callerUserId ) ;

    var target = await memberRepository.Query()
                   .FirstOrDefaultAsync( m => m.BrokerAccountId == accountId && m.AppUserId == targetUserId )
                 ?? throw new KeyNotFoundException(
                   $"User {targetUserId} is not a member of BrokerAccount {accountId}." ) ;

    var memberCount = await memberRepository.Query()
      .CountAsync( m => m.BrokerAccountId == accountId ) ;

    if ( memberCount <= 1 )
    {
      throw new BadRequestException(
        "Cannot remove the last member — delete the account instead." ) ;
    }

    memberRepository.Delete( target ) ;
    await memberRepository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<List<BrokerAccount>> GetAllAsync( long? userId = null )
  {
    IQueryable<BrokerAccount> query = repository.QueryActive()
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
      .ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<BrokerAccount?> GetByIdAsync( long id ) =>
    await repository.QueryActive()
      .Include( b => b.SpendAccount )
      .Include( b => b.Members )
      .ThenInclude( m => m.AppUser )
      .FirstOrDefaultAsync( b => b.Id == id ) ;

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var account = await repository.GetActiveByIdOrThrowAsync( id ) ;
    repository.SoftDelete( account ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task UpdateAsync( BrokerAccount account )
  {
    await spendRepository.GetActiveByIdOrThrowAsync( account.SpendAccountId ) ;
    repository.Update( account ) ;
    await repository.SaveAsync() ;
  }

  private async Task EnsureCallerIsMemberAsync( long accountId, long callerUserId )
  {
    var exists = await repository.QueryActive()
      .AnyAsync( b => b.Id == accountId
                      && b.Members.Any( m => m.AppUserId == callerUserId && ! m.AppUser.IsDeleted ) ) ;

    if ( ! exists )
    {
      throw new KeyNotFoundException( $"BrokerAccount {accountId} not found for user {callerUserId}." ) ;
    }
  }

  private async Task EnsureUserIsSpendAccountMemberAsync( long spendAccountId, long userId )
  {
    var ok = await spendRepository.QueryActive()
      .AnyAsync( s => s.Id == spendAccountId
                      && s.Members.Any( m => m.AppUserId == userId && ! m.AppUser.IsDeleted ) ) ;

    if ( ! ok )
    {
      throw new KeyNotFoundException( $"SpendAccount {spendAccountId} not found for user {userId}." ) ;
    }
  }

  private async Task<List<BrokerAccountMember>> LoadActiveMembersAsync( long accountId ) =>
    await memberRepository.Query()
      .AsNoTracking()
      .Include( m => m.AppUser )
      .Where( m => m.BrokerAccountId == accountId && ! m.AppUser.IsDeleted )
      .OrderBy( m => m.JoinedAt )
      .ToListAsync() ;
}
