using Fiolyn.Entities ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Data ;

public class AppDbContext( DbContextOptions<AppDbContext> options ) : DbContext( options )
{
  public DbSet<AllowedUser> AllowedUsers => Set<AllowedUser>() ;

  protected override void OnModelCreating( ModelBuilder modelBuilder ) =>
    modelBuilder.Entity<AllowedUser>( e =>
    {
      e.ToTable( "allowed_users" ) ;
      e.HasKey( x => x.Id ) ;
      e.Property( x => x.TelegramId ).HasColumnName( "telegram_id" ).IsRequired() ;
      e.Property( x => x.Label ).HasColumnName( "label" ) ;
      e.Property( x => x.CreatedAt ).HasColumnName( "created_at" ).HasDefaultValueSql( "NOW()" ) ;
      e.HasIndex( x => x.TelegramId ).IsUnique() ;
    } ) ;
}