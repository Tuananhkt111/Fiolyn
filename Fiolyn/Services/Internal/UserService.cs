using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   User profile reads plus admin-only management operations.
/// </summary>
public class UserService( IRepository<AppUser> repository, ILogger<UserService> logger ) : IUserService
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
    logger.LogInformation( "Admin flag for user {UserId} set to {IsAdmin}", id, isAdmin ) ;
    return user ;
  }

  /// <inheritdoc />
  public async Task<AppUser> UpdateTelegramIdAsync( long id, long? telegramId )
  {
    var user = await GetByIdAsync( id ) ;

    if ( telegramId is not null )
    {
      var takenByOther = await repository.QueryActive()
        .AnyAsync( u => u.TelegramId == telegramId && u.Id != id ) ;
      if ( takenByOther )
      {
        throw new ConflictException( $"TelegramId {telegramId} is already linked to another user." ) ;
      }
    }

    user.TelegramId = telegramId ;
    repository.Update( user ) ;
    await repository.SaveAsync() ;
    logger.LogInformation(
      "TelegramId for user {UserId} set to {TelegramId}", id, telegramId?.ToString() ?? "null" ) ;
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
