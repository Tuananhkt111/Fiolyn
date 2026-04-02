using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.Services.External.Telegram ;

namespace Fiolyn.ServiceRegisters ;

public class ScopedServicesRegister : IServiceRegister
{
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.AddScoped<ITelegramMessageService, TelegramMessageService>() ;
  }
}