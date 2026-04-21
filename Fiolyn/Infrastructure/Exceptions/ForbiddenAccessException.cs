namespace Fiolyn.Infrastructure.Exceptions ;

/// <summary>
///   Thrown when an authenticated caller lacks the permissions required to perform an action.
///   Mapped to HTTP 403 by <see cref="GlobalExceptionHandler" />.
/// </summary>
public class ForbiddenAccessException : Exception
{
  public ForbiddenAccessException() : base( "Forbidden" )
  {
  }

  public ForbiddenAccessException( string message ) : base( message )
  {
  }
}
