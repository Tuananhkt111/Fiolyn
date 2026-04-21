using Fiolyn.Entities ;
using Fiolyn.Entities.Base ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Data ;

public class AppDbContext( DbContextOptions<AppDbContext> options ) : DbContext( options )
{
  public DbSet<TelegramUserInfo> TelegramUserInfo => Set<TelegramUserInfo>() ;
  public DbSet<BrokerAccount> BrokerAccount => Set<BrokerAccount>() ;
  public DbSet<StockTransaction> StockTransaction => Set<StockTransaction>() ;
  public DbSet<AppUser> AppUser => Set<AppUser>() ;
  public DbSet<AllowedEmail> AllowedEmail => Set<AllowedEmail>() ;
  public DbSet<RefreshToken> RefreshToken => Set<RefreshToken>() ;

  public override int SaveChanges()
  {
    SetDefaultValues() ;
    return base.SaveChanges() ;
  }

  public override Task<int> SaveChangesAsync( CancellationToken cancellationToken = new() )
  {
    SetDefaultValues() ;
    return base.SaveChangesAsync( cancellationToken ) ;
  }

  private void SetDefaultValues()
  {
    var entries = ChangeTracker.Entries()
      .Where( e => e.State is EntityState.Added or EntityState.Modified ) ;

    var now = DateTime.UtcNow ;

    foreach ( var entry in entries )
    {
      if ( entry.Entity is IAuditable auditableEntity )
      {
        if ( entry.State == EntityState.Added )
        {
          auditableEntity.Created = now ;
        }

        auditableEntity.LastModified = now ;
      }
    }
  }
}
