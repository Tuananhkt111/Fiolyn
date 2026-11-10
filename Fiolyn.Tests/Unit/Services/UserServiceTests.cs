using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;
using Microsoft.Extensions.Logging ;
using MockQueryable ;
using Npgsql ;
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
    _repository.GetByIdOrThrowAsync( 1L ).ThrowsAsync( new KeyNotFoundException() ) ;
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
    _repository.GetByIdOrThrowAsync( 1L ).Returns( user ) ;
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
    _repository.GetByIdOrThrowAsync( 1L ).Returns( user ) ;
    var sut = CreateSut() ;

    var result = await sut.SetAdminAsync( 1, true ) ;

    result.IsAdmin.Should().BeTrue() ;
    _repository.Received( 1 ).Update( user ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task UpdateTelegramIdAsync_SetsAndSaves()
  {
    var user = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s",
      TelegramId = null
    } ;
    _repository.GetByIdOrThrowAsync( 1L ).Returns( user ) ;
    _repository.Query().Returns( new List<AppUser>
    {
      user
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateTelegramIdAsync( 1, 12345L ) ;

    result.TelegramId.Should().Be( 12345L ) ;
    _repository.Received( 1 ).Update( user ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task UpdateTelegramIdAsync_NullClearsAndSkipsUniquenessCheck()
  {
    var user = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s",
      TelegramId = 999L
    } ;
    _repository.GetByIdOrThrowAsync( 1L ).Returns( user ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateTelegramIdAsync( 1, null ) ;

    result.TelegramId.Should().BeNull() ;
    _repository.DidNotReceive().Query() ;
    _repository.Received( 1 ).Update( user ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task UpdateTelegramIdAsync_MissingUser_ThrowsKeyNotFound()
  {
    _repository.GetByIdOrThrowAsync( 1L ).ThrowsAsync( new KeyNotFoundException() ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.UpdateTelegramIdAsync( 1, 12345L ) )
      .Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task UpdateTelegramIdAsync_TakenByOtherUser_ThrowsConflictAndDoesNotSave()
  {
    var caller = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s",
      TelegramId = null
    } ;
    var other = new AppUser
    {
      Id = 2,
      Email = "b@b.com",
      GoogleSub = "s2",
      TelegramId = 12345L
    } ;
    _repository.GetByIdOrThrowAsync( 1L ).Returns( caller ) ;
    _repository.Query().Returns( new List<AppUser>
    {
      caller,
      other
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.UpdateTelegramIdAsync( 1, 12345L ) )
      .Should().ThrowAsync<ConflictException>() ;
    await _repository.DidNotReceive().SaveAsync() ;
  }

  [Fact]
  public async Task UpdateTelegramIdAsync_RaceLost_UniqueViolationMappedToConflict()
  {
    // The pre-check passed (no other user holds this TelegramId yet) but a concurrent caller
    // claimed it before SaveAsync flushed. Postgres surfaces SqlState 23505 inside a
    // DbUpdateException; the service must remap to ConflictException so the caller sees a 409.
    var user = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s",
      TelegramId = null
    } ;
    _repository.GetByIdOrThrowAsync( 1L ).Returns( user ) ;
    _repository.Query().Returns( new List<AppUser>
    {
      user
    }.BuildMock() ) ;
    var pgException = new PostgresException(
      "duplicate key value violates unique constraint",
      "ERROR",
      "ERROR",
      PostgresErrorCodes.UniqueViolation ) ;
    _repository.SaveAsync()
      .ThrowsAsync( new DbUpdateException( "unique violation", pgException ) ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.UpdateTelegramIdAsync( 1, 12345L ) )
      .Should().ThrowAsync<ConflictException>() ;
  }

  [Fact]
  public async Task UpdateTelegramIdAsync_SelfReassign_IsNotAConflict()
  {
    var caller = new AppUser
    {
      Id = 1,
      Email = "a@b.com",
      GoogleSub = "s",
      TelegramId = 12345L
    } ;
    _repository.GetByIdOrThrowAsync( 1L ).Returns( caller ) ;
    _repository.Query().Returns( new List<AppUser>
    {
      caller
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateTelegramIdAsync( 1, 12345L ) ;

    result.TelegramId.Should().Be( 12345L ) ;
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
    _repository.GetByIdOrThrowAsync( 1L ).Returns( user ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 1 ) ;

    _repository.Received( 1 ).SoftDelete( user ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }
}
