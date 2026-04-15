namespace Fiolyn.Controllers.DTOs.Responses ;

/// <summary>
///   Standard API response wrapper providing a consistent shape for all endpoints.
/// </summary>
public record ApiResponse<T>(
  bool Success,
  T? Data = default,
  string? Error = null ) ;