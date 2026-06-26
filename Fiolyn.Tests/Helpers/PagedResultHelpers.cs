namespace Fiolyn.Tests.Helpers ;

/// <summary>
///   Test helpers for building <see cref="PagedResult{T}" /> instances when stubbing service mocks.
/// </summary>
public static class PagedResultHelpers
{
  /// <summary>
  ///   Wraps the supplied items in a single-page <see cref="PagedResult{T}" /> with sensible default
  ///   page metadata. Used by controller tests where we don't actually exercise pagination — we just
  ///   need a paged shape to satisfy the new service contract.
  /// </summary>
  public static PagedResult<T> AsSinglePage<T>( this IReadOnlyList<T> items, int page = 1, int pageSize = 20 ) =>
    new(items, page, pageSize, items.Count) ;

  /// <summary>
  ///   Builds a single-page <see cref="PagedResult{T}" /> from a params array. Lets test sites
  ///   write <c>SinglePage&lt;T&gt;( a, b )</c> without ceremony around collection expressions /
  ///   IReadOnlyList casts.
  /// </summary>
  public static PagedResult<T> SinglePage<T>( params T[] items ) => new(items, 1, 20, items.Length) ;

  public static PagedResult<T> EmptyPage<T>( int page = 1, int pageSize = 20 ) => new([], page, pageSize, 0) ;
}
