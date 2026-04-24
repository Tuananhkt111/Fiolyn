namespace Fiolyn.Infrastructure.Exceptions ;

/// <summary>
///   Thrown when a request is well-formed but semantically invalid (e.g. would leave a
///   spend account with zero members). Mapped to HTTP 400 by <see cref="GlobalExceptionHandler" />.
/// </summary>
public class BadRequestException : Exception
{
  public BadRequestException() : base( "Bad Request" )
  {
  }

  public BadRequestException( string message ) : base( message )
  {
  }
}
