namespace Fiolyn.Utils ;

public static class DateTimeUtils
{
  /// <summary>
  ///   Parses a date string to UTC DateTime, falling back to <see cref="DateTime.UtcNow" /> if null or invalid.
  /// </summary>
  public static DateTime ParseDateOrUtcNow( string? dateStr )
  {
    if ( ! string.IsNullOrWhiteSpace( dateStr )
         && DateTime.TryParse( dateStr, out var parsed ) )
    {
      return DateTime.SpecifyKind( parsed, DateTimeKind.Utc ) ;
    }

    return DateTime.UtcNow ;
  }
}