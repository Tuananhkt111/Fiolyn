using System.Text ;
using System.Text.Json ;
using Fiolyn.ServiceRegisters.Settings ;
using Microsoft.Extensions.Options ;

namespace Fiolyn.Services.External.Notion ;

/// <summary>
/// Integrates with the Notion API to create database pages.
/// </summary>
public class NotionService( IOptions<NotionSettings> notionSettings, IHttpClientFactory httpClientFactory, ILogger<NotionService> logger )
{
  private readonly NotionSettings _notionSettings = notionSettings.Value ;

  /// <summary>
  /// Appends a new page to the configured Notion database.
  /// </summary>
  /// <param name="text">The text content to save as a new Notion page.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  /// <exception cref="InvalidOperationException">Thrown if the Notion API request fails.</exception>
  /// <remarks>
  /// Creates a new page in the configured Notion database with the given text as the page title.
  /// All pages are created with a single Title property.
  /// </remarks>
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

    var http = httpClientFactory.CreateClient() ;
    http.DefaultRequestHeaders.Add( "Authorization", $"Bearer {_notionSettings.NotionApiKey}" ) ;
    http.DefaultRequestHeaders.Add( "Notion-Version", "2022-06-28" ) ;

    var json = JsonSerializer.Serialize( payload ) ;
    using var content = new StringContent( json, Encoding.UTF8, "application/json" ) ;
    var response = await http.PostAsync( "https://api.notion.com/v1/pages", content ) ;

    if ( ! response.IsSuccessStatusCode )
    {
      var body = await response.Content.ReadAsStringAsync() ;
      logger.LogError( "Notion API error {Status}: {Body}", (int)response.StatusCode, body ) ;
      throw new InvalidOperationException( $"Notion API failed with status {response.StatusCode}: {body}" ) ;
    }
  }
}
