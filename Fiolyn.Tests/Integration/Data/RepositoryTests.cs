using Fiolyn.Data.Repositories ;
using Fiolyn.Tests.Integration.Helpers ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Tests.Integration.Data ;

public class RepositoryTests : IDisposable
{
  private readonly TestDbContext _dbContext = TestDbContextFactory.Create() ;

  public void Dispose() => _dbContext.Dispose() ;

  private Repository<TestEntity> CreateRepository() => new(_dbContext) ;

  [Fact]
  public async Task Query_ExcludesSoftDeletedEntities()
  {
    var repo = CreateRepository() ;
    await repo.AddAsync( new TestEntity
    {
      Id = 1L
    } ) ;
    await repo.AddAsync( new TestEntity
    {
      Id = 2L
    } ) ;
    await repo.SaveAsync() ;

    repo.SoftDelete( ( await repo.GetByIdAsync( 2L ) )! ) ;
    await repo.SaveAsync() ;

    var result = await repo.Query().ToListAsync() ;

    result.Should().ContainSingle( e => e.Id == 1L ) ;
    result.Should().NotContain( e => e.Id == 2L ) ;
  }

  [Fact]
  public async Task SoftDelete_SetsIsDeletedTrue()
  {
    var repo = CreateRepository() ;
    await repo.AddAsync( new TestEntity
    {
      Id = 20L
    } ) ;
    await repo.SaveAsync() ;

    repo.SoftDelete( ( await repo.GetByIdAsync( 20L ) )! ) ;
    await repo.SaveAsync() ;

    var saved = await repo.GetByIdAsync( 20L ) ;
    saved!.IsDeleted.Should().BeTrue() ;
  }

  [Fact]
  public async Task Restore_SetsIsDeletedFalse()
  {
    var repo = CreateRepository() ;
    var entity = new TestEntity
    {
      Id = 30L
    } ;
    await repo.AddAsync( entity ) ;
    await repo.SaveAsync() ;

    repo.SoftDelete( entity ) ;
    await repo.SaveAsync() ;

    repo.Restore( entity ) ;
    await repo.SaveAsync() ;

    var saved = await repo.GetByIdAsync( 30L ) ;
    saved!.IsDeleted.Should().BeFalse() ;
  }

  [Fact]
  public async Task Delete_RemovesEntityFromDatabase()
  {
    var repo = CreateRepository() ;
    var entity = new TestEntity
    {
      Id = 40L
    } ;
    await repo.AddAsync( entity ) ;
    await repo.SaveAsync() ;

    repo.Delete( entity ) ;
    await repo.SaveAsync() ;

    var saved = await repo.GetByIdAsync( 40L ) ;
    saved.Should().BeNull() ;
  }

  [Fact]
  public async Task GetByIdAsync_ReturnsSoftDeletedEntity()
  {
    // FindAsync bypasses query filters by design — only GetByIdOrThrowAsync enforces "active".
    var repo = CreateRepository() ;
    var entity = new TestEntity
    {
      Id = 70L
    } ;
    await repo.AddAsync( entity ) ;
    await repo.SaveAsync() ;

    repo.SoftDelete( entity ) ;
    await repo.SaveAsync() ;

    var result = await repo.GetByIdAsync( 70L ) ;

    result.Should().NotBeNull() ;
    result!.IsDeleted.Should().BeTrue() ;
  }

  [Fact]
  public async Task GetByIdAsync_ReturnsNullForNonExistentEntity()
  {
    var repo = CreateRepository() ;

    var result = await repo.GetByIdAsync( 999L ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task GetByIdOrThrowAsync_ReturnsActiveEntity()
  {
    var repo = CreateRepository() ;
    await repo.AddAsync( new TestEntity
    {
      Id = 80L
    } ) ;
    await repo.SaveAsync() ;

    var result = await repo.GetByIdOrThrowAsync( 80L ) ;

    result.Id.Should().Be( 80L ) ;
  }

  [Fact]
  public async Task GetByIdOrThrowAsync_ThrowsForSoftDeletedEntity()
  {
    var repo = CreateRepository() ;
    var entity = new TestEntity
    {
      Id = 81L
    } ;
    await repo.AddAsync( entity ) ;
    await repo.SaveAsync() ;

    repo.SoftDelete( entity ) ;
    await repo.SaveAsync() ;

    var act = async () => await repo.GetByIdOrThrowAsync( 81L ) ;
    await act.Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task GetByIdOrThrowAsync_ThrowsForNonExistentEntity()
  {
    var repo = CreateRepository() ;

    var act = async () => await repo.GetByIdOrThrowAsync( 999L ) ;
    await act.Should().ThrowAsync<KeyNotFoundException>() ;
  }
}
