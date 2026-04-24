namespace Fiolyn.Infrastructure.Exceptions ;

/// <summary>
///   Thrown when a write conflicts with existing state (e.g. a uniqueness constraint).
///   Mapped to HTTP 409 by <see cref="GlobalExceptionHandler" />.
/// </summary>
public class ConflictException : Exception
{
  public ConflictException() : base( "Conflict" )
  {
  }

  public ConflictException( string message ) : base( message )
  {
  }
}
