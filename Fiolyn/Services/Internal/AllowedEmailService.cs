using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   CRUD over the admin-managed email whitelist.
/// </summary>
public class AllowedEmailService(
  IRepository<AllowedEmail> repository,
  ILogger<AllowedEmailService> logger ) : IAllowedEmailService
{
  /// <inheritdoc />
  public async Task<List<AllowedEmail>> GetAllAsync() =>
    await repository.QueryActive().AsNoTracking().OrderBy( a => a.Email ).ToListAsync() ;

  /// <inheritdoc />
  public async Task<AllowedEmail> CreateAsync( string email, string? note, long? invitedByUserId )
  {
    var normalizedEmail = AppUser.NormalizeEmail( email ) ;

    var existing = await repository.QueryActive()
      .FirstOrDefaultAsync( a => a.Email == normalizedEmail ) ;
    if ( existing is not null )
    {
      throw new InvalidOperationException( $"Email {normalizedEmail} is already whitelisted." ) ;
    }

    var entry = new AllowedEmail
    {
      Email = normalizedEmail,
      Note = note,
      InvitedByUserId = invitedByUserId
    } ;

    await repository.AddAsync( entry ) ;
    await repository.SaveAsync() ;
    logger.LogInformation( "Email {Email} whitelisted by user {InvitedByUserId}",
      normalizedEmail, invitedByUserId ) ;
    return entry ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var entry = await repository.GetActiveByIdOrThrowAsync( id ) ;
    repository.SoftDelete( entry ) ;
    await repository.SaveAsync() ;
    logger.LogInformation( "Whitelist entry {Id} ({Email}) removed", id, entry.Email ) ;
  }
}
