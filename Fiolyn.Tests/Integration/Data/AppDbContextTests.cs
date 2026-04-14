using Fiolyn.Tests.Integration.Helpers ;
using FluentAssertions ;

namespace Fiolyn.Tests.Integration.Data ;

public class AppDbContextTests
{
  [Fact]
  public async Task SaveChangesAsync_OnInsert_SetsCreatedAndLastModified()
  {
    await using var dbContext = TestDbContextFactory.Create() ;
    var before = DateTime.UtcNow ;

    dbContext.TestEntities.Add( new TestEntity
    {
      Id = 100L
    } ) ;
    await dbContext.SaveChangesAsync() ;

    var entity = await dbContext.TestEntities.FindAsync( 100L ) ;
    entity!.Created.Should().BeOnOrAfter( before ) ;
    entity.LastModified.Should().BeOnOrAfter( before ) ;
  }

  [Fact]
  public async Task SaveChangesAsync_OnUpdate_UpdatesLastModifiedButNotCreated()
  {
    await using var dbContext = TestDbContextFactory.Create() ;

    dbContext.TestEntities.Add( new TestEntity
    {
      Id = 101L
    } ) ;
    await dbContext.SaveChangesAsync() ;

    var entity = await dbContext.TestEntities.FindAsync( 101L ) ;
    var originalCreated = entity!.Created ;

    entity.IsDeleted = true ;
    dbContext.TestEntities.Update( entity ) ;
    await dbContext.SaveChangesAsync() ;

    var updated = await dbContext.TestEntities.FindAsync( 101L ) ;
    updated!.Created.Should().Be( originalCreated ) ;
    updated.LastModified.Should().BeOnOrAfter( originalCreated ) ;
  }
}