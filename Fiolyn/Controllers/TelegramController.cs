using Fiolyn.Services.External ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Controllers ;

/// <summary>
/// Handles incoming Telegram webhook messages.
/// </summary>
[ApiController]
[Route( "api/[controller]" )]
public class TelegramController( TelegramMessageService messageService ) : ControllerBase
{
  /// <summary>
  /// Processes a Telegram webhook update.
  /// </summary>
  /// <remarks>
  /// Always returns HTTP 200 OK per Telegram webhook requirements.
  /// Processing failures are logged but don't affect the response status.
  /// </remarks>
  /// <returns>HTTP 200 OK with processing result.</returns>
  [HttpPost]
  public async Task<IActionResult> PostAsync()
  {
    using var reader = new StreamReader( Request.Body ) ;
    var raw = await reader.ReadToEndAsync() ;

    var result = await messageService.ProcessWebhookAsync( raw ) ;

    // Always return 200 to Telegram, regardless of success/skip
    return Ok( result ) ;
  }
}