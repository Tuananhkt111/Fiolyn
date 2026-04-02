using Fiolyn.Data ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

public class AllowedUsersService( AppDbContext db )
{
  /// <summary>
  /// Checks if a Telegram user is in the allowed users list.
  /// </summary>
  public async Task<bool> IsUserAllowedAsync( long telegramId ) => await db.AllowedUsers.AnyAsync( u => u.TelegramId == telegramId ) ;
}