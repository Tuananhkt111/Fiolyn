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

public class AllowedEmailServiceTests
{
  private readonly ILogger<AllowedEmailService> _logger = Substitute.For<ILogger<AllowedEmailService>>() ;
  private readonly IRepository<AllowedEmail> _repository = Substitute.For<IRepository<AllowedEmail>>() ;

  private AllowedEmailService CreateSut() => new(_repository, _logger) ;

  private void SetupExisting( params string[] emails )
  {
    var list = emails.Select( e => new AllowedEmail
    {
      Email = e
    } ).ToList().BuildMock() ;
    _repository.Query().Returns( list ) ;
  }

  [Fact]
  [Trait( "Story", "US-01" )]
  public async Task CreateAsync_NormalizesEmailAndPersists()
  {
    SetupExisting() ;
    var sut = CreateSut() ;

    var entry = await sut.CreateAsync( "  BOB@Example.COM  ", "note", 5 ) ;

    entry.Email.Should().Be( "bob@example.com" ) ;
    entry.Note.Should().Be( "note" ) ;
    entry.InvitedByUserId.Should().Be( 5 ) ;
    await _repository.Received( 1 ).AddAsync( Arg.Any<AllowedEmail>() ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task CreateAsync_DuplicateEmail_Throws()
  {
    SetupExisting( "dup@example.com" ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.CreateAsync( "dup@example.com", null, null ) )
      .Should().ThrowAsync<ConflictException>() ;
  }

  [Fact]
  public async Task CreateAsync_RaceLost_UniqueViolationMappedToConflict()
  {
    // The pre-check passed (no row matched the normalized email yet) but a concurrent admin
    // whitelisted the same email before SaveAsync flushed. The unique index throws Postgres
    // SqlState 23505; the service must remap to ConflictException so the caller sees a 409.
    SetupExisting() ;
    var pgException = new PostgresException(
      "duplicate key value violates unique constraint",
      "ERROR",
      "ERROR",
      PostgresErrorCodes.UniqueViolation ) ;
    _repository.SaveAsync()
      .ThrowsAsync( new DbUpdateException( "unique violation", pgException ) ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.CreateAsync( "race@example.com", null, null ) )
      .Should().ThrowAsync<ConflictException>() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_Missing_ThrowsKeyNotFound()
  {
    _repository.GetByIdOrThrowAsync( 99L ).ThrowsAsync( new KeyNotFoundException() ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.SoftDeleteAsync( 99 ) )
      .Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_Existing_SoftDeletes()
  {
    var entry = new AllowedEmail
    {
      Id = 1,
      Email = "x@y.com"
    } ;
    _repository.GetByIdOrThrowAsync( 1L ).Returns( entry ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 1 ) ;

    _repository.Received( 1 ).SoftDelete( entry ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }
}
