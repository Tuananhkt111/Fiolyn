using Fiolyn.Services.External.Telegram.Results ;

namespace Fiolyn.Services.External.Telegram ;

/// <summary>
///   Defines operations for processing Telegram webhook messages.
/// </summary>
public interface ITelegramMessageService
{
  /// <summary>
  ///   Processes a raw Telegram webhook JSON payload.
  /// </summary>
  /// <param name="rawJson">The raw JSON body from the Telegram webhook.</param>
  /// <returns>A <see cref="ProcessResult" /> indicating success, skip, or error.</returns>
  /// <remarks>
  ///   Validates the sender against the allowed users list before saving to Notion.
  ///   Returns appropriate result codes for all outcomes (success, skipped, error).
  /// </remarks>
  Task<ProcessResult> ProcessWebhookAsync( string rawJson ) ;
}
