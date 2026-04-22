using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using Microsoft.Extensions.Logging ;
using NSubstitute ;
using NSubstitute.ExceptionExtensions ;

namespace Fiolyn.Tests.Unit.Services ;

public class UserServiceTests
{
  private readonly ILogger<UserService> _logger = Substitute.For<ILogger<UserService>>() ;
  private readonly IRepository<AppUser> _repository = Substitute.For<IRepository<AppUser>>() ;

  private UserService CreateSut() => new(_repository, _logger) ;

  [Fact]
  public async Task GetByIdAsync_Missing_ThrowsKeyNotFound()
  {
    _repository.GetActiveByIdOrThrowAsync( 1L ).ThrowsAsync( new KeyNotFoundException() ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.GetByIdAsync( 1 ) )
      .Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task UpdateDisplayNameAsync_UpdatesAndSaves()
  {
    var user = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s",
      DisplayName = "Old"
    } ;
    _repository.GetActiveByIdOrThrowAsync( 1L ).Returns( user ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateDisplayNameAsync( 1, "New" ) ;

    result.DisplayName.Should().Be( "New" ) ;
    _repository.Received( 1 ).Update( user ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task SetAdminAsync_TogglesFlag()
  {
    var user = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s",
      IsAdmin = false
    } ;
    _repository.GetActiveByIdOrThrowAsync( 1L ).Returns( user ) ;
    var sut = CreateSut() ;

    var result = await sut.SetAdminAsync( 1, true ) ;

    result.IsAdmin.Should().BeTrue() ;
    _repository.Received( 1 ).Update( user ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_Existing_SoftDeletes()
  {
    var user = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s"
    } ;
    _repository.GetActiveByIdOrThrowAsync( 1L ).Returns( user ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 1 ) ;

    _repository.Received( 1 ).SoftDelete( user ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }
}
