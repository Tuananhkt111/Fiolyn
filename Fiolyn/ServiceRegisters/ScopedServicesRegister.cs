using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.Services ;

namespace Fiolyn.ServiceRegisters ;

public class ScopedServicesRegister : IServiceRegister
{
  public int Order => 1 ;

  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.AddScoped<AllowedUsersService>() ;
    services.AddScoped<TelegramMessageService>() ;
  }
}