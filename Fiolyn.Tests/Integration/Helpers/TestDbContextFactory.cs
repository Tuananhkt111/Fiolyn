using Fiolyn.Data ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Tests.Integration.Helpers ;

public static class TestDbContextFactory
{
  public static TestDbContext Create()
  {
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseInMemoryDatabase( Guid.NewGuid().ToString() )
      .Options ;

    return new TestDbContext( options ) ;
  }
}
