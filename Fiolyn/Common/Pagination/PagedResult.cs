namespace Fiolyn.Common.Pagination ;

/// <summary>
///   Wrapper for paginated list responses. Serialized as
///   <c>{ items, page, pageSize, totalCount, totalPages }</c> via the global CamelCase contract
///   resolver.
/// </summary>
public record PagedResult<T>(
  IReadOnlyList<T> Items,
  int Page,
  int PageSize,
  long TotalCount )
{
  public int TotalPages => PageSize == 0 ? 0 : ( int )Math.Ceiling( TotalCount / ( double )PageSize ) ;
}
