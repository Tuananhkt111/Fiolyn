using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;
using NSubstitute.ExceptionExtensions ;

namespace Fiolyn.Tests.Unit.Services ;

public class BrokerAccountServiceTests
{
  private readonly IRepository<BrokerAccount> _repository = Substitute.For<IRepository<BrokerAccount>>() ;

  private BrokerAccountService CreateSut() => new(_repository) ;

  private void SetupActive( params BrokerAccount[] brokers ) =>
    _repository.QueryActive().Returns( brokers.ToList().BuildMock() ) ;

  [Fact]
  public async Task CreateAsync_AddsAndSaves()
  {
    var entity = new BrokerAccount
    {
      Name = "VPS"
    } ;
    var sut = CreateSut() ;

    var result = await sut.CreateAsync( entity ) ;

    result.Should().BeSameAs( entity ) ;
    await _repository.Received( 1 ).AddAsync( entity ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task GetAllAsync_ReturnsOrderedByName()
  {
    SetupActive(
      new BrokerAccount
      {
        Id = 2,
        Name = "TCBS"
      },
      new BrokerAccount
      {
        Id = 1,
        Name = "VPS"
      } ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    result.Select( b => b.Name ).Should().ContainInOrder( "TCBS", "VPS" ) ;
  }

  [Fact]
  public async Task ExistsAsync_Present_ReturnsTrue()
  {
    SetupActive( new BrokerAccount
    {
      Id = 5,
      Name = "X"
    } ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsAsync( 5 ) ).Should().BeTrue() ;
  }

  [Fact]
  public async Task ExistsAsync_Missing_ReturnsFalse()
  {
    SetupActive() ;
    var sut = CreateSut() ;

    ( await sut.ExistsAsync( 5 ) ).Should().BeFalse() ;
  }

  [Fact]
  public async Task GetByIdAsync_DelegatesToRepository()
  {
    var entity = new BrokerAccount
    {
      Id = 3,
      Name = "Y"
    } ;
    _repository.GetActiveByIdAsync( 3L ).Returns( entity ) ;
    var sut = CreateSut() ;

    ( await sut.GetByIdAsync( 3 ) ).Should().BeSameAs( entity ) ;
  }

  [Fact]
  public async Task UpdateAsync_UpdatesAndSaves()
  {
    var entity = new BrokerAccount
    {
      Id = 1,
      Name = "Z"
    } ;
    var sut = CreateSut() ;

    await sut.UpdateAsync( entity ) ;

    _repository.Received( 1 ).Update( entity ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_Missing_ThrowsKeyNotFound()
  {
    _repository.GetActiveByIdOrThrowAsync( 99L ).ThrowsAsync( new KeyNotFoundException() ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.SoftDeleteAsync( 99 ) )
      .Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_Existing_SoftDeletes()
  {
    var entity = new BrokerAccount
    {
      Id = 1,
      Name = "X"
    } ;
    _repository.GetActiveByIdOrThrowAsync( 1L ).Returns( entity ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 1 ) ;

    _repository.Received( 1 ).SoftDelete( entity ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }
}
