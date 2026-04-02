using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.ServiceRegisters.Settings ;
using Fiolyn.Services.External.Notion ;

namespace Fiolyn.ServiceRegisters ;

public class NotionServiceRegister : IServiceRegister
{
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.Configure<NotionSettings>( configuration.GetSection( "NotionSettings" ) ) ;

    services.AddHttpClient<NotionService>( "notion", client =>
    {
      client.BaseAddress = new Uri( "https://api.notion.com/v1/" ) ;
      client.DefaultRequestHeaders.Add( "Notion-Version", "2022-06-28" ) ;
      client.Timeout = TimeSpan.FromSeconds( 30 ) ;
    } ) ;

    services.AddSingleton<INotionService>( sp => sp.GetRequiredService<NotionService>() ) ;
  }
}