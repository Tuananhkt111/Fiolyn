using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Common.Pagination ;

/// <summary>
///   Extensions that turn an <see cref="IQueryable{T}" /> into a <see cref="PagedResult{T}" /> via
///   <c>LongCountAsync</c> + <c>Skip</c>/<c>Take</c>/<c>ToListAsync</c>, and project an existing
///   <see cref="PagedResult{T}" /> into a different element type while preserving page metadata.
///   Includes are ignored by <c>LongCountAsync</c>, so callers pass the post-<c>Where</c>/
///   post-<c>OrderBy</c> query as-is — there is no need to strip eager-loads first.
/// </summary>
public static class PagedQueryExtensions
{
  /// <summary>
  ///   Materializes one page of <paramref name="query" /> alongside the unpaged total count.
  /// </summary>
  public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
    this IQueryable<T> query, PaginationQuery pagination )
  {
    var total = await query.LongCountAsync() ;
    var items = await query
      .Skip( ( pagination.Page - 1 ) * pagination.PageSize )
      .Take( pagination.PageSize )
      .ToListAsync() ;
    return new PagedResult<T>( items, pagination.Page, pagination.PageSize, total ) ;
  }

  /// <summary>
  ///   Slices an already-materialized list into a <see cref="PagedResult{T}" />. Use this for
  ///   in-memory aggregations (e.g. obligations rollup) that can't be expressed as a query but
  ///   still want the same paged contract. Caller is responsible for sorting deterministically
  ///   before calling.
  /// </summary>
  public static PagedResult<T> ToPagedResult<T>(
    this IReadOnlyList<T> source, PaginationQuery pagination )
  {
    var skip = ( pagination.Page - 1 ) * pagination.PageSize ;
    var slice = skip >= source.Count
      ? []
      : source.Skip( skip ).Take( pagination.PageSize ).ToList() ;
    return new PagedResult<T>( slice, pagination.Page, pagination.PageSize, source.Count ) ;
  }

  /// <summary>
  ///   Projects each item of a <see cref="PagedResult{T}" /> through <paramref name="selector" />
  ///   while preserving page metadata.
  /// </summary>
  public static PagedResult<TOut> Map<TIn, TOut>(
    this PagedResult<TIn> source, Func<TIn, TOut> selector ) =>
    new(source.Items.Select( selector ).ToList(), source.Page, source.PageSize, source.TotalCount) ;
}
