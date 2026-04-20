using Fiolyn.Entities ;

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
  Task SoftDeleteAsync( long id ) ;
}