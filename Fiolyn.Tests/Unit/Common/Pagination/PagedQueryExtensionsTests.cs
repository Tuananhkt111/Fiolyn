using FluentAssertions ;
using MockQueryable ;

namespace Fiolyn.Tests.Unit.Common.Pagination ;

/// <summary>
///   Focused tests for the offset-pagination primitives. The async path is exercised against a
///   <see cref="MockQueryable" /> queryable so it goes through the same <c>LongCountAsync</c> +
///   <c>ToListAsync</c> calls that EF Core would in production. The in-memory path
///   (<c>ToPagedResult</c>) is exercised separately for the obligations / aggregation case.
/// </summary>
public class PagedQueryExtensionsTests
{
  private static List<Row> FiveRows() => [new(1), new(2), new(3), new(4), new(5)] ;

  [Fact]
  public async Task ToPagedResultAsync_FirstPage_ReturnsSliceAndTotal()
  {
    var page = await FiveRows().BuildMock()
      .ToPagedResultAsync( new PaginationQuery
      {
        Page = 1,
        PageSize = 2
      } ) ;

    page.Items.Select( r => r.Value ).Should().Equal( 1, 2 ) ;
    page.Page.Should().Be( 1 ) ;
    page.PageSize.Should().Be( 2 ) ;
    page.TotalCount.Should().Be( 5L ) ;
    page.TotalPages.Should().Be( 3 ) ;
  }

  [Fact]
  public async Task ToPagedResultAsync_LastPage_ReturnsRemainder()
  {
    var page = await FiveRows().BuildMock()
      .ToPagedResultAsync( new PaginationQuery
      {
        Page = 3,
        PageSize = 2
      } ) ;

    page.Items.Select( r => r.Value ).Should().Equal( 5 ) ;
    page.TotalCount.Should().Be( 5L ) ;
  }

  [Fact]
  public async Task ToPagedResultAsync_PastEnd_ReturnsEmpty_WithCorrectMetadata()
  {
    var page = await FiveRows().BuildMock()
      .ToPagedResultAsync( new PaginationQuery
      {
        Page = 10,
        PageSize = 2
      } ) ;

    page.Items.Should().BeEmpty() ;
    page.Page.Should().Be( 10 ) ;
    page.TotalCount.Should().Be( 5L ) ;
    page.TotalPages.Should().Be( 3 ) ;
  }

  [Fact]
  public void ToPagedResult_OnList_SlicesAndPreservesTotal()
  {
    var page = FiveRows().ToPagedResult( new PaginationQuery
    {
      Page = 2,
      PageSize = 2
    } ) ;

    page.Items.Select( r => r.Value ).Should().Equal( 3, 4 ) ;
    page.TotalCount.Should().Be( 5L ) ;
    page.TotalPages.Should().Be( 3 ) ;
  }

  [Fact]
  public void Map_ProjectsItems_PreservesPageMetadata()
  {
    var source = new PagedResult<int>( [1, 2, 3], 2, 5, 13L ) ;

    var mapped = source.Map( x => x * 10 ) ;

    mapped.Items.Should().Equal( 10, 20, 30 ) ;
    mapped.Page.Should().Be( 2 ) ;
    mapped.PageSize.Should().Be( 5 ) ;
    mapped.TotalCount.Should().Be( 13L ) ;
    mapped.TotalPages.Should().Be( 3 ) ;
  }

  [Fact]
  public void TotalPages_IsCeiling_OfCountOverPageSize()
  {
    new PagedResult<int>( [], 1, 10, 0L ).TotalPages.Should().Be( 0 ) ;
    new PagedResult<int>( [], 1, 10, 1L ).TotalPages.Should().Be( 1 ) ;
    new PagedResult<int>( [], 1, 10, 10L ).TotalPages.Should().Be( 1 ) ;
    new PagedResult<int>( [], 1, 10, 11L ).TotalPages.Should().Be( 2 ) ;
    new PagedResult<int>( [], 1, 10, 100L ).TotalPages.Should().Be( 10 ) ;
  }

  private sealed record Row( int Value ) ;
}
