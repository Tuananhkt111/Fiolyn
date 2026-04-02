namespace Fiolyn.Services.External.Telegram ;

/// <summary>
/// Result of processing a Telegram webhook message.
/// </summary>
public class ProcessResult
{
  /// <summary>
  /// Gets whether the message was successfully processed.
  /// </summary>
  public bool IsSuccess { get ; private set ; }

  /// <summary>
  /// Gets the result message describing the outcome.
  /// </summary>
  public string Message { get ; private set ; }

  private ProcessResult( bool success, string message )
  {
    IsSuccess = success ;
    Message = message ;
  }

  /// <summary>
  /// Creates a successful result.
  /// </summary>
  public static ProcessResult Success() => new( true, "Success" ) ;

  /// <summary>
  /// Creates a skipped result.
  /// </summary>
  public static ProcessResult Skipped( string reason ) => new( false, $"Skipped: {reason}" ) ;

  /// <summary>
  /// Creates an error result.
  /// </summary>
  public static ProcessResult Error( string error ) => new( false, $"Error: {error}" ) ;
}
