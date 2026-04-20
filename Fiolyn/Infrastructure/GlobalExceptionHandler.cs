using Fiolyn.Infrastructure.Exceptions ;
using Microsoft.AspNetCore.Diagnostics ;
using Microsoft.AspNetCore.Mvc ;

namespace Fiolyn.Infrastructure ;

/// <summary>
///   Centralized exception handler that maps known exceptions to appropriate HTTP status codes
///   and returns RFC 7807 ProblemDetails responses.
/// </summary>
public class GlobalExceptionHandler( ILogger<GlobalExceptionHandler> logger ) : IExceptionHandler
{
  public async ValueTask<bool> TryHandleAsync(
    HttpContext httpContext,
    Exception exception,
    CancellationToken cancellationToken )
  {
    var (statusCode, title) = exception switch
    {
      KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
      UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
      ForbiddenAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
      _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
    } ;

    if ( statusCode == StatusCodes.Status500InternalServerError )
    {
      logger.LogError( exception, "Unhandled exception: {Message}", exception.Message ) ;
    }

    httpContext.Response.StatusCode = statusCode ;

    await httpContext.Response.WriteAsJsonAsync( new ProblemDetails
    {
      Status = statusCode,
      Title = title,
      Detail = title
    }, cancellationToken ) ;

    return true ;
  }
}