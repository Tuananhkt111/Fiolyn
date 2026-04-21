using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Data.Seeds ;

/// <summary>
///   Startup extension that applies pending EF Core migrations.
///   Invoked once from <c>Program.cs</c> after <c>builder.Build()</c>.
/// </summary>
public static class DatabaseInitializer
{
  public static async Task InitializeDatabaseAsync( this WebApplication app )
  {
    using var scope = app.Services.CreateScope() ;

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>() ;
    await db.Database.MigrateAsync() ;
  }
}
