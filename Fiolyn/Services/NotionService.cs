using System.Text ;
using System.Text.Json ;

namespace Fiolyn.Services ;

public class NotionService
{
  private readonly string _databaseId ;
  private readonly HttpClient _http ;
  private readonly ILogger<NotionService> _logger ;

  public NotionService( string apiKey, string databaseId, ILogger<NotionService> logger )
  {
    _databaseId = databaseId ;
    _logger = logger ;
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
    var response = await _http.PostAsync( "https://api.notion.com/v1/pages", content ) ;

    if ( ! response.IsSuccessStatusCode )
    {
      var body = await response.Content.ReadAsStringAsync() ;
      _logger.LogError( "Notion API error {Status}: {Body}", (int)response.StatusCode, body ) ;
    }
  }
}