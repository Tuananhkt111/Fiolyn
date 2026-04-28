using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;
using Npgsql ;

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
    await repository.Query().AsNoTracking().OrderBy( a => a.Email ).ToListAsync() ;

  /// <inheritdoc />
  public async Task<AllowedEmail> CreateAsync( string email, string? note, long? invitedByUserId )
  {
    var normalizedEmail = AppUser.NormalizeEmail( email ) ;

    var existing = await repository.Query()
      .FirstOrDefaultAsync( a => a.Email == normalizedEmail ) ;
    if ( existing is not null )
    {
      throw new ConflictException( $"Email {normalizedEmail} is already whitelisted." ) ;
    }

    var entry = new AllowedEmail
    {
      Email = normalizedEmail,
      Note = note,
      InvitedByUserId = invitedByUserId
    } ;

    await repository.AddAsync( entry ) ;
    try
    {
      await repository.SaveAsync() ;
    }
    catch ( DbUpdateException ex )
      when ( ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } )
    {
      throw new ConflictException( $"Email {normalizedEmail} is already whitelisted." ) ;
    }

    logger.LogInformation( "Email {Email} whitelisted by user {InvitedByUserId}",
      normalizedEmail, invitedByUserId ) ;
    return entry ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var entry = await repository.GetByIdOrThrowAsync( id ) ;
    repository.SoftDelete( entry ) ;
    await repository.SaveAsync() ;
    logger.LogInformation( "Whitelist entry {Id} ({Email}) removed", id, entry.Email ) ;
  }
}
