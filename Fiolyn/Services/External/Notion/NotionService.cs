using System.Text ;
using System.Text.Json ;
using Fiolyn.ServiceRegisters.Settings ;
using Microsoft.Extensions.Options ;

namespace Fiolyn.Services.External.Notion ;

/// <summary>
///   Integrates with the Notion API to create database pages.
/// </summary>
public class NotionService(
  IOptions<NotionSettings> notionSettings,
  HttpClient httpClient,
  ILogger<NotionService> logger ) : INotionService
{
  private readonly NotionSettings _notionSettings = notionSettings.Value ;

  /// <inheritdoc />
  public async Task AppendAsync( string text )
  {
    var payload = new
    {
      parent = new
      {
        database_id = _notionSettings.NotionDatabaseId
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
    using var response = await httpClient.PostAsync( "pages", content ) ;

    if ( ! response.IsSuccessStatusCode )
    {
      var body = await response.Content.ReadAsStringAsync() ;
      logger.LogError( "Notion API error {Status}: {Body}", ( int )response.StatusCode, body ) ;
      throw new InvalidOperationException( $"Notion API failed with status {response.StatusCode}: {body}" ) ;
    }

    logger.LogDebug( "Appended Notion page (text length {Length})", text.Length ) ;
  }
}
