using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.ServiceRegisters.Settings ;
using Microsoft.EntityFrameworkCore ;
using Microsoft.Extensions.Options ;

namespace Fiolyn.Data.Seeds ;

/// <summary>
///   Ensures the configured initial-admin email is present on the <see cref="AllowedEmail" />
///   whitelist so the owner can sign in via Google the first time. Idempotent.
/// </summary>
public class InitialAdminSeeder(
  IRepository<AllowedEmail> allowedEmailRepository,
  IOptions<InitialAdminSettings> settings,
  ILogger<InitialAdminSeeder> logger )
{
  public async Task SeedAsync()
  {
    var configured = settings.Value.Email ;
    if ( string.IsNullOrWhiteSpace( configured ) )
    {
      return ;
    }

    var normalized = AppUser.NormalizeEmail( configured ) ;

    var exists = await allowedEmailRepository.QueryActive()
      .AnyAsync( a => a.Email == normalized ) ;
    if ( exists )
    {
      return ;
    }

    await allowedEmailRepository.AddAsync( new AllowedEmail
    {
      Email = normalized,
      Note = "InitialAdmin (seeded)",
      InvitedByUserId = null
    } ) ;
    await allowedEmailRepository.SaveAsync() ;

    logger.LogInformation( "Seeded initial admin email into AllowedEmail whitelist: {Email}", normalized ) ;
  }
}