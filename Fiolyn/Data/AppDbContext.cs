using Fiolyn.Entities ;
using Fiolyn.Entities.Base ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Data ;

public class AppDbContext( DbContextOptions<AppDbContext> options ) : DbContext( options )
{
  public DbSet<BrokerAccount> BrokerAccount => Set<BrokerAccount>() ;
  public DbSet<BrokerAccountMember> BrokerAccountMember => Set<BrokerAccountMember>() ;
  public DbSet<StockTransaction> StockTransaction => Set<StockTransaction>() ;
  public DbSet<SpendAccount> SpendAccount => Set<SpendAccount>() ;
  public DbSet<SpendAccountMember> SpendAccountMember => Set<SpendAccountMember>() ;
  public DbSet<AppUser> AppUser => Set<AppUser>() ;
  public DbSet<AllowedEmail> AllowedEmail => Set<AllowedEmail>() ;
  public DbSet<RefreshToken> RefreshToken => Set<RefreshToken>() ;

  protected override void OnModelCreating( ModelBuilder modelBuilder )
  {
    base.OnModelCreating( modelBuilder ) ;

    // BrokerAccount → SpendAccount: required FK, but Restrict on delete (no [DeleteBehavior]
    // attribute exists — Fluent is the only option). The default Cascade for required relationships
    // would (a) form a multi-cascade chain into BrokerAccount/BrokerAccountMember that EF flags as
    // a dependency loop, and (b) silently delete every broker if a spend account is hard-deleted.
    // SpendAccountService blocks soft-delete while any active broker references it.
    modelBuilder.Entity<BrokerAccount>()
      .HasOne( b => b.SpendAccount )
      .WithMany()
      .HasForeignKey( b => b.SpendAccountId )
      .OnDelete( DeleteBehavior.Restrict ) ;
  }

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
