using System.Net.Http.Headers ;
using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.ServiceRegisters.Settings ;
using Fiolyn.Services.External.Notion ;

namespace Fiolyn.ServiceRegisters ;

public class NotionServiceRegister : IServiceRegister
{
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    var notionSection = configuration.GetSection( "NotionSettings" ) ;
    services.Configure<NotionSettings>( notionSection ) ;

    services.AddHttpClient<INotionService, NotionService>( client =>
    {
      client.BaseAddress = new Uri( "https://api.notion.com/v1/" ) ;
      client.DefaultRequestHeaders.Add( "Notion-Version", "2022-06-28" ) ;
      client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue( "Bearer", notionSection[ "NotionApiKey" ] ) ;
      client.Timeout = TimeSpan.FromSeconds( 30 ) ;
    } ) ;
  }
}
