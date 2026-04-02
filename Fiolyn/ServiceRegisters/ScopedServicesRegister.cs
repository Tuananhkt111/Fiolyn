using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.Services.External.Telegram ;
using Fiolyn.Services.Internal ;

namespace Fiolyn.ServiceRegisters ;

public class ScopedServicesRegister : IServiceRegister
{
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.AddScoped<AllowedUsersService>() ;
    services.AddScoped<TelegramMessageService>() ;
  }
}