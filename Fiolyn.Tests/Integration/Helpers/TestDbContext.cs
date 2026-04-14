using Fiolyn.Data ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Tests.Integration.Helpers ;

/// <summary>
///   Extends AppDbContext with a test-only DbSet so integration tests
///   exercise generic repository and audit logic without using production entities.
/// </summary>
public class TestDbContext( DbContextOptions<AppDbContext> options ) : AppDbContext( options )
{
  public DbSet<TestEntity> TestEntities => Set<TestEntity>() ;
}