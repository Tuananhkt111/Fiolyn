using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.ServiceRegisters.Settings ;
using Fiolyn.Services.External.Notion ;

namespace Fiolyn.ServiceRegisters ;

public class NotionServiceRegister : IServiceRegister
{
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.Configure<NotionSettings>( configuration.GetSection( "NotionSettings" ) ) ;
    services.AddSingleton<NotionService>() ;
  }
}