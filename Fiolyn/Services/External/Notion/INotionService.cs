namespace Fiolyn.Services.External.Notion ;

/// <summary>
///   Defines operations for integrating with the Notion API.
/// </summary>
public interface INotionService
{
  /// <summary>
  ///   Appends a new page to the configured Notion database.
  /// </summary>
  /// <param name="text">The text content to save as a new Notion page.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  /// <exception cref="InvalidOperationException">Thrown if the Notion API request fails.</exception>
  /// <remarks>
  ///   Creates a new page in the configured Notion database with the given text as the page title.
  ///   All pages are created with a single Title property.
  /// </remarks>
  Task AppendAsync( string text ) ;
}
