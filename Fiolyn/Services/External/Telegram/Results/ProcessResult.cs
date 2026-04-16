namespace Fiolyn.Services.External.Telegram.Results ;

/// <summary>
///   Result of processing a Telegram webhook message.
/// </summary>
public record ProcessResult( bool IsSuccess, string Message )
{
  /// <summary>
  ///   Creates a successful result.
  /// </summary>
  public static ProcessResult Success() => new(true, "Success") ;

  /// <summary>
  ///   Creates a skipped result.
  /// </summary>
  public static ProcessResult Skipped( string reason ) => new(false, $"Skipped: {reason}") ;

  /// <summary>
  ///   Creates an error result.
  /// </summary>
  public static ProcessResult Error( string error ) => new(false, $"Error: {error}") ;
}