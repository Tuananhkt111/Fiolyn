using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Reads and updates <see cref="AppUser" /> records. Mix of self-service (`me`) and admin operations.
/// </summary>
public interface IUserService
{
  Task<AppUser> GetByIdAsync( long id ) ;
  Task<List<AppUser>> GetAllAsync() ;
  Task<AppUser> UpdateDisplayNameAsync( long id, string? displayName ) ;
  Task<AppUser> SetAdminAsync( long id, bool isAdmin ) ;

  /// <summary>
  ///   Sets or clears the Telegram user ID linked to an <see cref="AppUser" />. Pass null to clear.
  ///   Throws <see cref="KeyNotFoundException" /> if the user doesn't exist (or is soft-deleted);
  ///   throws <see cref="ConflictException" /> if another active user already owns that TelegramId.
  /// </summary>
  Task<AppUser> UpdateTelegramIdAsync( long id, long? telegramId ) ;

  Task SoftDeleteAsync( long id ) ;
}
