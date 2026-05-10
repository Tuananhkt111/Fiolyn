using Fiolyn.Common.Pagination ;
using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Admin CRUD over the <see cref="AllowedEmail" /> whitelist.
/// </summary>
public interface IAllowedEmailService
{
  Task<PagedResult<AllowedEmail>> GetAllAsync( PaginationQuery pagination ) ;
  Task<AllowedEmail> CreateAsync( string email, string? note, long? invitedByUserId ) ;
  Task SoftDeleteAsync( long id ) ;
}
