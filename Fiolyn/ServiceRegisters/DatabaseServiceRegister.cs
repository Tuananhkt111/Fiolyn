using Fiolyn.Data ;
using Fiolyn.ServiceRegisters.Base ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.ServiceRegisters ;

public class DatabaseServiceRegister : IServiceRegister
{
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    var connectionString = configuration.GetConnectionString( "NeonDatabaseUrl" ) ;
    services.AddDbContext<AppDbContext>( options =>
      options.UseNpgsql( connectionString ) ) ;

    services.AddDbContextFactory<AppDbContext>( options => options.UseNpgsql( connectionString ),
      ServiceLifetime.Scoped ) ;
  }
}
