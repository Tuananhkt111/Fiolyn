using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;
using NSubstitute.ExceptionExtensions ;

namespace Fiolyn.Tests.Unit.Services ;

public class AllowedEmailServiceTests
{
  private readonly IRepository<AllowedEmail> _repository = Substitute.For<IRepository<AllowedEmail>>() ;

  private AllowedEmailService CreateSut() => new(_repository) ;

  private void SetupExisting( params string[] emails )
  {
    var list = emails.Select( e => new AllowedEmail
    {
      Email = e
    } ).ToList().BuildMock() ;
    _repository.QueryActive().Returns( list ) ;
  }

  [Fact]
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
      .Should().ThrowAsync<InvalidOperationException>() ;
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
    var entry = new AllowedEmail
    {
      Id = 1,
      Email = "x@y.com"
    } ;
    _repository.GetActiveByIdOrThrowAsync( 1L ).Returns( entry ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 1 ) ;

    _repository.Received( 1 ).SoftDelete( entry ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }
}