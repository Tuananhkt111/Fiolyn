using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Manages user-owned spend accounts. All read/delete operations are scoped by user.
/// </summary>
public class SpendAccountService( IRepository<SpendAccount> repository ) : ISpendAccountService
{
  /// <inheritdoc />
  public async Task<SpendAccount> CreateAsync( SpendAccount account )
  {
    await repository.AddAsync( account ) ;
    await repository.SaveAsync() ;
    return account ;
  }

  /// <inheritdoc />
  public async Task<List<SpendAccount>> GetAllForUserAsync( long userId ) =>
    await repository.QueryActive()
      .AsNoTracking()
      .Where( a => a.AppUserId == userId )
      .OrderBy( a => a.Name )
      .ToListAsync() ;

  /// <inheritdoc />
  public async Task<SpendAccount?> GetByIdForUserAsync( long id, long userId ) =>
    await repository.QueryActive()
      .FirstOrDefaultAsync( a => a.Id == id && a.AppUserId == userId ) ;

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
    repository.SoftDelete( account ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<List<SpendAccount>> GetAllAsync( long? userId = null )
  {
    IQueryable<SpendAccount> query = repository.QueryActive()
      .AsNoTracking()
      .Include( a => a.AppUser ) ;

    if ( userId is not null )
    {
      query = query.Where( a => a.AppUserId == userId.Value ) ;
    }

    return await query
      .OrderBy( a => a.AppUserId )
      .ThenBy( a => a.Name )
      .ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<SpendAccount?> GetByIdAsync( long id ) =>
    await repository.QueryActive()
      .Include( a => a.AppUser )
      .FirstOrDefaultAsync( a => a.Id == id ) ;

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var account = await repository.GetActiveByIdOrThrowAsync( id ) ;
    repository.SoftDelete( account ) ;
    await repository.SaveAsync() ;
  }
}
