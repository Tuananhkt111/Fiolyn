using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   User profile reads plus admin-only management operations.
/// </summary>
public class UserService( IRepository<AppUser> repository ) : IUserService
{
  /// <inheritdoc />
  public Task<AppUser> GetByIdAsync( long id ) => repository.GetActiveByIdOrThrowAsync( id ) ;

  /// <inheritdoc />
  public async Task<List<AppUser>> GetAllAsync() =>
    await repository.QueryActive().AsNoTracking().OrderBy( u => u.Email ).ToListAsync() ;

  /// <inheritdoc />
  public async Task<AppUser> UpdateDisplayNameAsync( long id, string? displayName )
  {
    var user = await GetByIdAsync( id ) ;
    user.DisplayName = displayName ;
    repository.Update( user ) ;
    await repository.SaveAsync() ;
    return user ;
  }

  /// <inheritdoc />
  public async Task<AppUser> SetAdminAsync( long id, bool isAdmin )
  {
    var user = await GetByIdAsync( id ) ;
    user.IsAdmin = isAdmin ;
    repository.Update( user ) ;
    await repository.SaveAsync() ;
    return user ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var user = await GetByIdAsync( id ) ;
    repository.SoftDelete( user ) ;
    await repository.SaveAsync() ;
  }
}
