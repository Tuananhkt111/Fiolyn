using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
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
  IRepository<BrokerAccount> brokerRepository ) : ISpendAccountService
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
  public async Task<List<SpendAccount>> GetAllForUserAsync( long userId ) =>
    await repository.QueryActive()
      .AsNoTracking()
      .Where( a => a.Members.Any( m => m.AppUserId == userId && ! m.AppUser.IsDeleted ) )
      .OrderBy( a => a.Name )
      .ToListAsync() ;

  /// <inheritdoc />
  public async Task<SpendAccount?> GetByIdForUserAsync( long id, long userId ) =>
    await repository.QueryActive()
      .FirstOrDefaultAsync( a => a.Id == id
                                 && a.Members.Any( m => m.AppUserId == userId && ! m.AppUser.IsDeleted ) ) ;

  /// <inheritdoc />
  public async Task UpdateAsync( SpendAccount account )
  {
    repository.Update( account ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id, long userId )
  {
    var account = await GetByIdForUserAsync( id, userId )
                  ?? throw new KeyNotFoundException( $"SpendAccount {id} not found for user {userId}." ) ;
    await EnsureNotReferencedByActiveBrokerAsync( id ) ;
    repository.SoftDelete( account ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<List<SpendAccountMember>> GetMembersAsync( long accountId, long callerUserId )
  {
    await EnsureCallerIsMemberAsync( accountId, callerUserId ) ;
    return await LoadActiveMembersAsync( accountId ) ;
  }

  /// <inheritdoc />
  public async Task<List<SpendAccountMember>> AddMemberAsync( long accountId, long callerUserId, string email )
  {
    await EnsureCallerIsMemberAsync( accountId, callerUserId ) ;

    var normalized = AppUser.NormalizeEmail( email ) ;
    var target = await userRepository.QueryActive()
                   .FirstOrDefaultAsync( u => u.Email == normalized )
                 ?? throw new KeyNotFoundException( $"User with email {normalized} not found." ) ;

    var alreadyMember = await memberRepository.Query()
      .AnyAsync( m => m.SpendAccountId == accountId && m.AppUserId == target.Id ) ;

    if ( ! alreadyMember )
    {
      await memberRepository.AddAsync( new SpendAccountMember
      {
        SpendAccountId = accountId,
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
                   .FirstOrDefaultAsync( m => m.SpendAccountId == accountId && m.AppUserId == targetUserId )
                 ?? throw new KeyNotFoundException(
                   $"User {targetUserId} is not a member of SpendAccount {accountId}." ) ;

    var memberCount = await memberRepository.Query()
      .CountAsync( m => m.SpendAccountId == accountId ) ;

    if ( memberCount <= 1 )
    {
      throw new BadRequestException(
        "Cannot remove the last member — delete the account instead." ) ;
    }

    memberRepository.Delete( target ) ;
    await memberRepository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<List<SpendAccount>> GetAllAsync( long? userId = null )
  {
    IQueryable<SpendAccount> query = repository.QueryActive()
      .AsNoTracking()
      .Include( a => a.Members )
      .ThenInclude( m => m.AppUser ) ;

    if ( userId is not null )
    {
      query = query.Where( a => a.Members.Any( m => m.AppUserId == userId.Value ) ) ;
    }

    return await query
      .OrderBy( a => a.Name )
      .ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<SpendAccount?> GetByIdAsync( long id ) =>
    await repository.QueryActive()
      .Include( a => a.Members )
      .ThenInclude( m => m.AppUser )
      .FirstOrDefaultAsync( a => a.Id == id ) ;

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var account = await repository.GetActiveByIdOrThrowAsync( id ) ;
    await EnsureNotReferencedByActiveBrokerAsync( id ) ;
    repository.SoftDelete( account ) ;
    await repository.SaveAsync() ;
  }

  private async Task EnsureNotReferencedByActiveBrokerAsync( long spendAccountId )
  {
    var stillReferenced = await brokerRepository.QueryActive()
      .AnyAsync( b => b.SpendAccountId == spendAccountId ) ;
    if ( stillReferenced )
    {
      throw new BadRequestException(
        $"SpendAccount {spendAccountId} is linked from one or more active broker accounts — unlink them first." ) ;
    }
  }

  private async Task EnsureCallerIsMemberAsync( long accountId, long callerUserId )
  {
    var exists = await repository.QueryActive()
      .AnyAsync( a => a.Id == accountId
                      && a.Members.Any( m => m.AppUserId == callerUserId && ! m.AppUser.IsDeleted ) ) ;

    if ( ! exists )
    {
      throw new KeyNotFoundException( $"SpendAccount {accountId} not found for user {callerUserId}." ) ;
    }
  }

  private async Task<List<SpendAccountMember>> LoadActiveMembersAsync( long accountId ) =>
    await memberRepository.Query()
      .AsNoTracking()
      .Include( m => m.AppUser )
      .Where( m => m.SpendAccountId == accountId && ! m.AppUser.IsDeleted )
      .OrderBy( m => m.JoinedAt )
      .ToListAsync() ;
}
