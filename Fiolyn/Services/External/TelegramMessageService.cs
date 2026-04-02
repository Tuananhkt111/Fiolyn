using System.Text.Json ;
using Fiolyn.Services.Internal ;

namespace Fiolyn.Services.External ;

/// <summary>
/// Processes incoming Telegram webhook messages and orchestrates saving to Notion.
/// </summary>
public class TelegramMessageService(
  AllowedUsersService allowedUsersService,
  NotionService notionService,
  ILogger<TelegramMessageService> logger )
{
  /// <summary>
  /// Processes a raw Telegram webhook JSON payload.
  /// </summary>
  /// <param name="rawJson">The raw JSON body from the Telegram webhook.</param>
  /// <returns>A <see cref="ProcessResult"/> indicating success, skip, or error.</returns>
  /// <remarks>
  /// Validates the sender against the allowed users list before saving to Notion.
  /// Returns appropriate result codes for all outcomes (success, skipped, error).
  /// </remarks>
  public async Task<ProcessResult> ProcessWebhookAsync( string rawJson )
  {
    try
    {
      using var doc = JsonDocument.Parse( rawJson ) ;
      var body = doc.RootElement ;

      // Extract message and sender
      if ( ! body.TryGetProperty( "message", out var message )
           || ! message.TryGetProperty( "from", out var from ) )
      {
        logger.LogInformation( "Webhook missing message or from field" ) ;
        return ProcessResult.Skipped( "Missing message or from field" ) ;
      }

      var fromId = from.GetProperty( "id" ).GetInt64() ;

      // Check if user is allowed
      var isAllowed = await allowedUsersService.IsUserAllowedAsync( fromId ) ;
      if ( ! isAllowed )
      {
        logger.LogInformation( "User {UserId} not in allowed list", fromId ) ;
        return ProcessResult.Skipped( "User not allowed" ) ;
      }

      // Extract text
      if ( ! message.TryGetProperty( "text", out var textElement ) )
      {
        logger.LogInformation( "Message from {UserId} has no text", fromId ) ;
        return ProcessResult.Skipped( "No text in message" ) ;
      }

      var text = textElement.GetString() ;
      if ( string.IsNullOrEmpty( text ) )
      {
        logger.LogInformation( "Empty text from user {UserId}", fromId ) ;
        return ProcessResult.Skipped( "Empty text" ) ;
      }

      // Save to Notion
      await notionService.AppendAsync( text ) ;
      logger.LogInformation( "Message from {UserId} saved to Notion", fromId ) ;

      return ProcessResult.Success() ;
    }
    catch ( JsonException ex )
    {
      logger.LogError( ex, "Failed to parse webhook JSON" ) ;
      return ProcessResult.Error( "Invalid JSON" ) ;
    }
    catch ( Exception ex )
    {
      logger.LogError( ex, "Unexpected error processing webhook" ) ;
      return ProcessResult.Error( "Internal error" ) ;
    }
  }

  public class ProcessResult
  {
    public bool IsSuccess { get ; private set ; }
    public string Message { get ; private set ; }

    private ProcessResult( bool success, string message )
    {
      IsSuccess = success ;
      Message = message ;
    }

    public static ProcessResult Success() => new( true, "Success" ) ;
    public static ProcessResult Skipped( string reason ) => new( false, $"Skipped: {reason}" ) ;
    public static ProcessResult Error( string error ) => new( false, $"Error: {error}" ) ;
  }
}