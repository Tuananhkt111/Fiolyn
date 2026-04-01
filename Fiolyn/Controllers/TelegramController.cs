using System.Text.Json ;
using Fiolyn.Data ;
using Fiolyn.Services ;
using Microsoft.AspNetCore.Mvc ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Controllers ;

[ApiController]
[Route( "api/[controller]" )]
public class TelegramController( AppDbContext db, NotionService notion ) : ControllerBase
{
  [HttpPost]
  public async Task<IActionResult> PostAsync()
  {
    using var reader = new StreamReader( Request.Body ) ;
    var raw = await reader.ReadToEndAsync() ;
    using var doc = JsonDocument.Parse( raw ) ;
    var body = doc.RootElement ;

    if ( ! body.TryGetProperty( "message", out var message )
         || ! message.TryGetProperty( "from", out var from ) )
    {
      return Ok( new
      {
        ok = true,
        skipped = true
      } ) ;
    }

    var fromId = from.GetProperty( "id" ).GetInt64() ;

    var isAllowed = await db.AllowedUsers.AnyAsync( u => u.TelegramId == fromId ) ;
    if ( ! isAllowed || ! message.TryGetProperty( "text", out var textElement ) )
    {
      return Ok( new
      {
        ok = true,
        skipped = true
      } ) ;
    }

    var text = textElement.GetString() ;
    if ( string.IsNullOrEmpty( text ) )
    {
      return Ok( new
      {
        ok = true,
        skipped = true
      } ) ;
    }

    await notion.AppendAsync( text ) ;
    return Ok( new
    {
      ok = true
    } ) ;
  }
}