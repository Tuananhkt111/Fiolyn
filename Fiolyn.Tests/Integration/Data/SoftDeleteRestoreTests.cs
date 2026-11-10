using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.External.Google ;
using Fiolyn.Services.Internal ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Tests.Integration.Helpers ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;
using Microsoft.Extensions.Logging ;
using NSubstitute ;

namespace Fiolyn.Tests.Integration.Data ;

/// <summary>
///   ADR 0024 against a real AppDbContext (EF in-memory provider), which applies the global soft-delete
///   query filters that mocks don't. Each test seeds a soft-deleted row and checks that the service
///   finds it through .IgnoreQueryFilters() and restores it. Without that call the filtered lookup misses
///   it and a second row is inserted: in-memory doesn't enforce unique indexes, so the row count shows it.
/// </summary>
public class SoftDeleteRestoreTests : IDisposable
{
  private readonly TestDbContext _db = TestDbContextFactory.Create() ;

  public void Dispose() => _db.Dispose() ;

  private Repository<T> Repo<T>() where T : class => new( _db ) ;

  private async Task<T> SeedAsync<T>( T entity ) where T : class
  {
    _db.Add( entity ) ;
    await _db.SaveChangesAsync() ;
    _db.ChangeTracker.Clear() ;
    return entity ;
  }

  [Fact]
  [Trait( "Story", "US-04" )]
  public async Task AllowedEmail_ReAddingARemovedAddress_RestoresTheSameRow()
  {
    var removed = await SeedAsync( new AllowedEmail
    {
      Email = "back@example.com",
      IsDeleted = true
    } ) ;
    var sut = new AllowedEmailService( Repo<AllowedEmail>(), Substitute.For<ILogger<AllowedEmailService>>() ) ;

    var entry = await sut.CreateAsync( "back@example.com", "returning", null ) ;

    entry.Id.Should().Be( removed.Id ) ;
    var rows = await _db.Set<AllowedEmail>().IgnoreQueryFilters().ToListAsync() ;
    rows.Should().ContainSingle().Which.IsDeleted.Should().BeFalse() ;
  }

  [Fact]
  [Trait( "Story", "US-16" )]
  public async Task TransactionCategory_ReCreatingADeletedName_RestoresTheSameRow()
  {
    var deleted = await SeedAsync( new TransactionCategory
    {
      Name = "Groceries",
      Kind = TransactionKind.Expense,
      IsDeleted = true
    } ) ;
    var sut = new TransactionCategoryService( Repo<TransactionCategory>() ) ;

    var entry = await sut.CreateAsync( "Groceries", TransactionKind.Expense ) ;

    entry.Id.Should().Be( deleted.Id ) ;
    ( await _db.Set<TransactionCategory>().IgnoreQueryFilters().CountAsync() ).Should().Be( 1 ) ;
  }

  [Fact]
  [Trait( "Story", "US-22" )]
  public async Task Counterparty_ReCreatingADeletedContact_RestoresTheSameRow()
  {
    await SeedAsync( new AppUser
    {
      Id = 7,
      Email = "u7@example.com",
      GoogleSub = "g7"
    } ) ;
    var deleted = await SeedAsync( new Counterparty
    {
      AppUserId = 7,
      Name = "bob",
      IsDeleted = true
    } ) ;
    var sut = new CounterpartyService( Repo<Counterparty>(), Repo<LendTransaction>(), Repo<BorrowingTransaction>() ) ;

    var entry = await sut.CreateAsync( "Bob", 7 ) ;

    entry.Id.Should().Be( deleted.Id ) ;
    var rows = await _db.Set<Counterparty>().IgnoreQueryFilters().ToListAsync() ;
    rows.Should().ContainSingle().Which.Name.Should().Be( "Bob" ) ;
  }

  [Fact]
  [Trait( "Story", "US-22" )]
  public async Task Lend_NamingADeletedContact_RestoresItInTheSameSave()
  {
    await SeedAsync( new AppUser
    {
      Id = 42,
      Email = "u42@example.com",
      GoogleSub = "g42"
    } ) ;
    await SeedAsync( new SpendAccount
    {
      Id = 5,
      Name = "Cash",
      Members = [new SpendAccountMember { AppUserId = 42, JoinedAt = DateTime.UtcNow }]
    } ) ;
    var deleted = await SeedAsync( new Counterparty
    {
      AppUserId = 42,
      Name = "Bob",
      IsDeleted = true
    } ) ;
    var sut = new LendTransactionService(
      Repo<LendTransaction>(), Repo<SpendAccount>(), Repo<TransactionCategory>(), Repo<Counterparty>() ) ;

    var lend = await sut.CreateAsync( new LendTransaction
    {
      SpendAccountId = 5,
      Amount = -100,
      Date = DateTime.UtcNow
    }, 42, "bob" ) ;

    lend.Counterparty.Id.Should().Be( deleted.Id ) ;
    var rows = await _db.Set<Counterparty>().IgnoreQueryFilters().ToListAsync() ;
    rows.Should().ContainSingle().Which.IsDeleted.Should().BeFalse() ;
  }

  [Fact]
  [Trait( "Story", "US-01" )]
  public async Task Login_DeletedUserWithAWhitelistedEmail_IsRefusedAndNoUserIsCreated()
  {
    await SeedAsync( new AllowedEmail
    {
      Email = "gone@example.com"
    } ) ;
    await SeedAsync( new AppUser
    {
      Email = "gone@example.com",
      GoogleSub = "sub-gone",
      IsDeleted = true
    } ) ;
    var google = Substitute.For<IGoogleTokenVerifier>() ;
    google.VerifyAsync( "token" ).Returns( new GooglePayload( "gone@example.com", "sub-gone", "Gone", "p.jpg" ) ) ;
    var sut = new AuthService( google, Substitute.For<IJwtService>(), Repo<AppUser>(), Repo<AllowedEmail>(),
      Repo<RefreshToken>(), Substitute.For<ILogger<AuthService>>() ) ;

    var act = () => sut.LoginWithGoogleAsync( "token" ) ;

    await act.Should().ThrowAsync<ForbiddenAccessException>() ;
    ( await _db.Set<AppUser>().IgnoreQueryFilters().CountAsync() ).Should().Be( 1 ) ;
  }
}
