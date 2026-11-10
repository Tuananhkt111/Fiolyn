using System.Net.Http.Headers ;
using Fiolyn.Data ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Hosting ;
using Microsoft.AspNetCore.Mvc.Testing ;
using Microsoft.EntityFrameworkCore ;
using Microsoft.EntityFrameworkCore.Diagnostics ;
using Microsoft.EntityFrameworkCore.Infrastructure ;
using Microsoft.EntityFrameworkCore.Storage ;
using Microsoft.Extensions.DependencyInjection ;

namespace Fiolyn.Tests.Integration.Helpers ;

/// <summary>
///   Hosts the real Fiolyn pipeline in memory: routing, JWT bearer authentication, the Admin policy,
///   model validation, the exception handler and the controllers. Only the database is swapped, for
///   EF's in-memory provider, so nothing touches Neon. The "Testing" environment keeps a local
///   appsettings.Development.json (real secrets) out of the run.
///   Each factory owns one in-memory store, shared by every request it serves.
/// </summary>
public class FiolynApiFactory : WebApplicationFactory<Program>
{
  private const string SigningKey = "test-signing-key-at-least-32-characters-long" ;

  private readonly InMemoryDatabaseRoot _databaseRoot = new() ;
  private readonly string _databaseName = Guid.NewGuid().ToString() ;

  protected override void ConfigureWebHost( IWebHostBuilder builder )
  {
    builder.UseEnvironment( "Testing" ) ;
    builder.UseSetting( "Jwt:SigningKey", SigningKey ) ;
    builder.UseSetting( "ConnectionStrings:NeonDatabaseUrl", "Host=unused" ) ;

    builder.ConfigureServices( services =>
    {
      foreach ( var descriptor in services.Where( IsDatabaseOption ).ToList() )
      {
        services.Remove( descriptor ) ;
      }

      services.AddDbContext<AppDbContext>( Configure ) ;
      services.AddDbContextFactory<AppDbContext>( Configure, ServiceLifetime.Scoped ) ;
    } ) ;
  }

  private void Configure( DbContextOptionsBuilder options ) =>
    options.UseInMemoryDatabase( _databaseName, _databaseRoot )
      .ConfigureWarnings( w => w.Ignore( InMemoryEventId.TransactionIgnoredWarning ) ) ;

  private static bool IsDatabaseOption( ServiceDescriptor descriptor ) =>
    descriptor.ServiceType == typeof( DbContextOptions<AppDbContext> )
    || descriptor.ServiceType == typeof( DbContextOptions )
    || descriptor.ServiceType == typeof( IDbContextOptionsConfiguration<AppDbContext> ) ;

  /// <summary>A client that sends no token.</summary>
  public HttpClient Anonymous() => CreateClient() ;

  /// <summary>
  ///   A client carrying an access token minted by the app's own <see cref="IJwtService" />, so it
  ///   passes the same signature, issuer, audience and lifetime checks a real token does.
  /// </summary>
  public HttpClient As( AppUser user )
  {
    using var scope = Services.CreateScope() ;
    var ( token, _ ) = scope.ServiceProvider.GetRequiredService<IJwtService>().CreateAccessToken( user ) ;
    var client = CreateClient() ;
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", token ) ;
    return client ;
  }

  /// <summary>Writes rows straight to the store the app reads from, then forgets them.</summary>
  public async Task SeedAsync( params object[] entities )
  {
    using var scope = Services.CreateScope() ;
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>() ;
    db.AddRange( entities ) ;
    await db.SaveChangesAsync() ;
  }

  /// <summary>Runs a query against a fresh context on the same store.</summary>
  public async Task<T> QueryAsync<T>( Func<AppDbContext, Task<T>> query )
  {
    using var scope = Services.CreateScope() ;
    return await query( scope.ServiceProvider.GetRequiredService<AppDbContext>() ) ;
  }
}
