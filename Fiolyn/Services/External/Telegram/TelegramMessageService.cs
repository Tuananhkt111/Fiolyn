using System.Text.Json ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.External.Notion ;
using Fiolyn.Services.External.Telegram.Results ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.External.Telegram ;

/// <summary>
///   Processes incoming Telegram webhook messages and orchestrates saving to Notion.
/// </summary>
public class TelegramMessageService(
  IRepository<AppUser> userRepository,
  INotionService notionService,
  IStockTransactionService stockTransactionService,
  ILogger<TelegramMessageService> logger ) : ITelegramMessageService
{
  /// <inheritdoc />
  public async Task<ProcessResult> ProcessWebhookAsync( string rawJson )
  {
    try
    {
      using var doc = JsonDocument.Parse( rawJson ) ;
      var body = doc.RootElement ;

      if ( ! body.TryGetProperty( "message", out var message )
           || ! message.TryGetProperty( "from", out var from ) )
      {
        logger.LogInformation( "Webhook missing message or from field" ) ;
        return ProcessResult.Skipped( "Missing message or from field" ) ;
      }

      var fromId = from.GetProperty( "id" ).GetInt64() ;

      var callerId = await userRepository.Query()
        .Where( u => u.TelegramId == fromId )
        .Select( u => ( long? )u.Id )
        .FirstOrDefaultAsync() ;
      if ( callerId is null )
      {
        logger.LogInformation( "Telegram user {TelegramId} not linked to any AppUser", fromId ) ;
        return ProcessResult.Skipped( "User not allowed" ) ;
      }

      if ( ! message.TryGetProperty( "text", out var textElement ) )
      {
        logger.LogInformation( "Message from {TelegramId} has no text", fromId ) ;
        return ProcessResult.Skipped( "No text in message" ) ;
      }

      var text = textElement.GetString() ;
      if ( string.IsNullOrEmpty( text ) )
      {
        logger.LogInformation( "Empty text from Telegram user {TelegramId}", fromId ) ;
        return ProcessResult.Skipped( "Empty text" ) ;
      }

      if ( text.StartsWith( "stock ", StringComparison.OrdinalIgnoreCase ) )
      {
        var transaction = await stockTransactionService.ParseMessageAsync( text ) ;
        if ( transaction is null )
        {
          logger.LogWarning( "Invalid stock message format from {TelegramId}: {Text}", fromId, text ) ;
          return ProcessResult.Error( "Invalid stock message format" ) ;
        }

        transaction.PayerId = callerId.Value ;
        try
        {
          await stockTransactionService.CreateAsync( transaction, callerId.Value ) ;
        }
        catch ( KeyNotFoundException ex )
        {
          logger.LogWarning( ex,
            "Telegram user {TelegramId} (AppUserId={UserId}) not a member of broker for {Symbol}",
            fromId, callerId.Value, transaction.Symbol ) ;
          return ProcessResult.Error( "Not authorized for broker" ) ;
        }

        logger.LogInformation( "Stock transaction from {TelegramId} saved: {Symbol} {Type}",
          fromId, transaction.Symbol, transaction.StockTransactionType ) ;
        return ProcessResult.Success() ;
      }

      await notionService.AppendAsync( text ) ;
      logger.LogInformation( "Message from Telegram user {TelegramId} saved to Notion", fromId ) ;

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
}
