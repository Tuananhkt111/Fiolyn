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
  public async Task QueryActive_ExcludesSoftDeletedEntities()
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

    var result = await repo.QueryActive().ToListAsync() ;

    result.Should().ContainSingle( e => e.Id == 1L ) ;
    result.Should().NotContain( e => e.Id == 2L ) ;
  }

  [Fact]
  public async Task QueryActive_ReturnsAllWhenNoneDeleted()
  {
    var repo = CreateRepository() ;
    await repo.AddAsync( new TestEntity
    {
      Id = 10L
    } ) ;
    await repo.AddAsync( new TestEntity
    {
      Id = 11L
    } ) ;
    await repo.SaveAsync() ;

    var result = await repo.QueryActive().ToListAsync() ;

    result.Should().HaveCount( 2 ) ;
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
  public async Task GetByIdAsync_ReturnsCorrectEntity()
  {
    var repo = CreateRepository() ;
    await repo.AddAsync( new TestEntity
    {
      Id = 50L
    } ) ;
    await repo.SaveAsync() ;

    var result = await repo.GetByIdAsync( 50L ) ;

    result.Should().NotBeNull() ;
    result!.Id.Should().Be( 50L ) ;
  }

  [Fact]
  public async Task GetActiveByIdAsync_ReturnsActiveEntity()
  {
    var repo = CreateRepository() ;
    await repo.AddAsync( new TestEntity
    {
      Id = 60L
    } ) ;
    await repo.SaveAsync() ;

    var result = await repo.GetActiveByIdAsync( 60L ) ;

    result.Should().NotBeNull() ;
    result!.Id.Should().Be( 60L ) ;
  }

  [Fact]
  public async Task GetActiveByIdAsync_ReturnsNullForSoftDeletedEntity()
  {
    var repo = CreateRepository() ;
    var entity = new TestEntity
    {
      Id = 70L
    } ;
    await repo.AddAsync( entity ) ;
    await repo.SaveAsync() ;

    repo.SoftDelete( entity ) ;
    await repo.SaveAsync() ;

    var result = await repo.GetActiveByIdAsync( 70L ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task GetActiveByIdAsync_ReturnsNullForNonExistentEntity()
  {
    var repo = CreateRepository() ;

    var result = await repo.GetActiveByIdAsync( 999L ) ;

    result.Should().BeNull() ;
  }
}
