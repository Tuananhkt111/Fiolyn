using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Common.Pagination ;

/// <summary>
///   Offset-based pagination query bound from the request URL via <c>[FromQuery]</c>. Defaults
///   apply when a parameter is absent. <c>[ApiController]</c> emits 400 automatically when the
///   <see cref="Range" /> attributes fail.
/// </summary>
public record PaginationQuery
{
  public const int DefaultPageSize = 20 ;
  public const int MaxPageSize = 100 ;

  [Range( 1, int.MaxValue )]
  public int Page { get ; init ; } = 1 ;

  [Range( 1, MaxPageSize )]
  public int PageSize { get ; init ; } = DefaultPageSize ;
}
