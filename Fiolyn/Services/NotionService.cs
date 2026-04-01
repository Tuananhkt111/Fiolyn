using System.Text ;
using System.Text.Json ;

namespace Fiolyn.Services ;

public class NotionService
{
  private readonly string _databaseId ;
  private readonly HttpClient _http ;

  public NotionService( string apiKey, string databaseId )
  {
    _databaseId = databaseId ;
    _http = new HttpClient() ;
    _http.DefaultRequestHeaders.Add( "Authorization", $"Bearer {apiKey}" ) ;
    _http.DefaultRequestHeaders.Add( "Notion-Version", "2022-06-28" ) ;
  }

  public async Task AppendAsync( string text )
  {
    var payload = new
    {
      parent = new
      {
        database_id = _databaseId
      },
      properties = new
      {
        Name = new
        {
          title = new[]
          {
            new
            {
              text = new
              {
                content = text
              }
            }
          }
        }
      }
    } ;

    var json = JsonSerializer.Serialize( payload ) ;
    using var content = new StringContent( json, Encoding.UTF8, "application/json" ) ;
    await _http.PostAsync( "https://api.notion.com/v1/pages", content ) ;
  }
}