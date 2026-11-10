using Fiolyn.Controllers.DTOs.Responses.Auth ;
using Fiolyn.Data ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.External.Google ;
using Fiolyn.Services.Internal ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;
using Microsoft.EntityFrameworkCore.Diagnostics ;
using Microsoft.EntityFrameworkCore.Storage ;
using Microsoft.Extensions.Logging.Abstractions ;
using NSubstitute ;

namespace Fiolyn.Tests.Integration.Data ;

/// <summary>
///   Real two-request races. Each "request" has its own <see cref="AppDbContext" />, repositories and
///   service over ONE shared EF in-memory store, so EF's own concurrency-token check decides the loser.
///   The interleaving is forced by <see cref="RunOtherRequestFirstInterceptor" />: request A reads its
///   rows, and when it starts saving, request B runs to completion first, so A's save meets a changed row.
///   NOTE: this is EF's in-memory provider, not Postgres. It checks <c>[ConcurrencyCheck]</c> tokens
///   like a relational provider but has no real transaction, so rollback atomicity is not proven here.
/// </summary>
public class ConcurrencyRaceTests
{
  private const string AtomicityNeedsRelationalProvider =
    "EF in-memory applies inserts before the failing concurrency-checked update (no transaction), so the "
    + "loser's added rows survive. On Postgres the single SaveChanges is one transaction and rolls back; "
    + "run against a relational provider to verify." ;

  private const string Email = "racer@example.com" ;

  private readonly InMemoryDatabaseRoot _root = new() ;
  private readonly string _dbName = Guid.NewGuid().ToString() ;

  private AppDbContext CreateContext( params IInterceptor[] interceptors )
  {
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseInMemoryDatabase( _dbName, _root )
      .AddInterceptors( interceptors )
      .Options ;
    return new AppDbContext( options ) ;
  }

  // ---------------------------------------------------------------- US-02 AC3

  [Fact]
  [Trait( "Story", "US-02" )]
  public async Task Refresh_TwoConcurrentRequestsOnSameToken_ExactlyOneSucceedsAndLoserGets401()
  {
    await SeedUserWithRefreshTokenAsync( "old-token" ) ;

    var jwt = CreateJwtStub() ;
    AuthResponse? winnerResponse = null ;
    var interceptor = new RunOtherRequestFirstInterceptor() ;

    // Request B (the winner): own context, own service, runs to completion inside A's save.
    interceptor.OtherRequest = async () =>
    {
      await using var ctxB = CreateContext() ;
      winnerResponse = await CreateAuthService( ctxB, jwt ).RefreshAsync( "old-token" ) ;
    } ;

    await using var ctxA = CreateContext( interceptor ) ;
    var requestA = CreateAuthService( ctxA, jwt ) ;

    var act = async () => await requestA.RefreshAsync( "old-token" ) ;

    await act.Should().ThrowAsync<UnauthorizedAccessException>() ;
    interceptor.OtherRequestRan.Should().BeTrue( "the race must actually have been interleaved" ) ;
    winnerResponse.Should().NotBeNull() ;

    await using var verify = CreateContext() ;
    var tokens = await verify.RefreshToken.IgnoreQueryFilters().AsNoTracking().ToListAsync() ;

    // The old token is revoked by the winner and points at the winner's replacement, which is active.
    var winnerHash = jwt.HashRefreshToken( winnerResponse!.RefreshToken ) ;
    var old = tokens.Single( t => t.TokenHash == jwt.HashRefreshToken( "old-token" ) ) ;
    old.RevokedAt.Should().NotBeNull() ;
    old.ReplacedByTokenHash.Should().Be( winnerHash ) ;
    tokens.Single( t => t.TokenHash == winnerHash ).RevokedAt.Should().BeNull() ;
  }

  [Fact( Skip = AtomicityNeedsRelationalProvider )]
  [Trait( "Story", "US-02" )]
  public async Task Refresh_LosingRequest_LeavesNoActiveTokenBehind()
  {
    await SeedUserWithRefreshTokenAsync( "old-token" ) ;
    var jwt = CreateJwtStub() ;
    var interceptor = new RunOtherRequestFirstInterceptor
    {
      OtherRequest = async () =>
      {
        await using var ctxB = CreateContext() ;
        await CreateAuthService( ctxB, jwt ).RefreshAsync( "old-token" ) ;
      }
    } ;

    await using var ctxA = CreateContext( interceptor ) ;
    var act = async () => await CreateAuthService( ctxA, jwt ).RefreshAsync( "old-token" ) ;
    await act.Should().ThrowAsync<UnauthorizedAccessException>() ;

    await using var verify = CreateContext() ;
    var active = await verify.RefreshToken.IgnoreQueryFilters().AsNoTracking()
      .Where( t => t.RevokedAt == null ).ToListAsync() ;
    active.Should().ContainSingle( "only the winner's new token may remain active" ) ;
  }

  // ---------------------------------------------------------------- US-17 AC6

  [Fact]
  [Trait( "Story", "US-17" )]
  public async Task SpendBalance_TwoConcurrentExpenses_OneSucceedsOtherGets409AndBalanceReflectsWinnerOnly()
  {
    const long startBalance = 1_000_000 ;
    var spendId = await SeedSpendAccountAsync( startBalance ) ;
    var interceptor = new RunOtherRequestFirstInterceptor() ;

    // Request B (winner): expense of 200_000 (stored negative).
    interceptor.OtherRequest = async () =>
    {
      await using var ctxB = CreateContext() ;
      await CreateExpenseService( ctxB ).CreateAsync( NewExpense( spendId, -200_000 ), 1 ) ;
    } ;

    // Request A (loser): expense of 50_000.
    await using var ctxA = CreateContext( interceptor ) ;
    var requestA = CreateExpenseService( ctxA ) ;

    var act = async () => await requestA.CreateAsync( NewExpense( spendId, -50_000 ), 1 ) ;

    await act.Should().ThrowAsync<ConflictException>() ;
    interceptor.OtherRequestRan.Should().BeTrue( "the race must actually have been interleaved" ) ;

    await using var verify = CreateContext() ;
    var account = await verify.SpendAccount.AsNoTracking().SingleAsync( s => s.Id == spendId ) ;
    account.Balance.Should().Be( 800_000 ) ;

  }

  [Fact( Skip = AtomicityNeedsRelationalProvider )]
  [Trait( "Story", "US-17" )]
  public async Task SpendBalance_LosingExpense_IsNotPersisted()
  {
    var spendId = await SeedSpendAccountAsync( 1_000_000 ) ;
    var interceptor = new RunOtherRequestFirstInterceptor
    {
      OtherRequest = async () =>
      {
        await using var ctxB = CreateContext() ;
        await CreateExpenseService( ctxB ).CreateAsync( NewExpense( spendId, -200_000 ), 1 ) ;
      }
    } ;

    await using var ctxA = CreateContext( interceptor ) ;
    var act = async () => await CreateExpenseService( ctxA ).CreateAsync( NewExpense( spendId, -50_000 ), 1 ) ;
    await act.Should().ThrowAsync<ConflictException>() ;

    await using var verify = CreateContext() ;
    var amounts = await verify.ExpenseTransaction.AsNoTracking().Select( t => t.Amount ).ToListAsync() ;
    amounts.Should().BeEquivalentTo( new long[] { -200_000 } ) ;
  }

  // ---------------------------------------------------------------- helpers

  private static ExpenseTransaction NewExpense( long spendId, long amount ) => new()
  {
    SpendAccountId = spendId,
    Amount = amount,
    Date = new DateTime( 2026, 1, 1, 0, 0, 0, DateTimeKind.Utc ),
    Description = "race"
  } ;

  private static ExpenseTransactionService CreateExpenseService( AppDbContext ctx ) => new(
    new Repository<ExpenseTransaction>( ctx ),
    new Repository<SpendAccount>( ctx ),
    new Repository<TransactionCategory>( ctx ),
    new Repository<Counterparty>( ctx ),
    new Repository<LendTransaction>( ctx ) ) ;

  private static AuthService CreateAuthService( AppDbContext ctx, IJwtService jwt ) => new(
    Substitute.For<IGoogleTokenVerifier>(),
    jwt,
    new Repository<AppUser>( ctx ),
    new Repository<AllowedEmail>( ctx ),
    new Repository<RefreshToken>( ctx ),
    NullLogger<AuthService>.Instance ) ;

  /// <summary>
  ///   Pure token plumbing, so a deterministic fake is fine: every new refresh token is unique and the
  ///   hash is a prefix of the raw token, which lets the test map a raw token to its stored hash.
  /// </summary>
  private static IJwtService CreateJwtStub()
  {
    var jwt = Substitute.For<IJwtService>() ;
    var counter = 0 ;
    jwt.HashRefreshToken( Arg.Any<string>() ).Returns( c => "hash:" + c.Arg<string>() ) ;
    jwt.CreateAccessToken( Arg.Any<AppUser>() )
      .Returns( _ => ( "access", DateTime.UtcNow.AddMinutes( 15 ) ) ) ;
    jwt.CreateRefreshToken().Returns( _ =>
    {
      var raw = "new-" + Interlocked.Increment( ref counter ) ;
      return ( raw, "hash:" + raw, DateTime.UtcNow.AddDays( 7 ) ) ;
    } ) ;
    return jwt ;
  }

  private async Task SeedUserWithRefreshTokenAsync( string rawToken )
  {
    await using var ctx = CreateContext() ;
    ctx.AllowedEmail.Add( new AllowedEmail { Email = Email } ) ;
    var user = new AppUser { Id = 1, Email = Email, GoogleSub = "sub-1" } ;
    ctx.AppUser.Add( user ) ;
    ctx.RefreshToken.Add( new RefreshToken
    {
      User = user,
      TokenHash = "hash:" + rawToken,
      ExpiresAt = DateTime.UtcNow.AddDays( 7 )
    } ) ;
    await ctx.SaveChangesAsync() ;
  }

  private async Task<long> SeedSpendAccountAsync( long balance )
  {
    await using var ctx = CreateContext() ;
    var user = new AppUser { Id = 1, Email = Email, GoogleSub = "sub-1" } ;
    var spend = new SpendAccount { Name = "Cash", Type = SpendAccountType.Cash, Balance = balance } ;
    ctx.AppUser.Add( user ) ;
    ctx.SpendAccount.Add( spend ) ;
    ctx.SpendAccountMember.Add( new SpendAccountMember { SpendAccount = spend, AppUser = user } ) ;
    await ctx.SaveChangesAsync() ;
    return spend.Id ;
  }

  /// <summary>
  ///   When its context starts its first save, runs <see cref="OtherRequest" /> to completion first.
  ///   By then the owning request has already read its rows, so its save conflicts with the other one.
  /// </summary>
  private sealed class RunOtherRequestFirstInterceptor : SaveChangesInterceptor
  {
    public Func<Task>? OtherRequest { get ; set ; }
    public bool OtherRequestRan { get ; private set ; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default )
    {
      if ( ! OtherRequestRan && OtherRequest is not null )
      {
        OtherRequestRan = true ;
        await OtherRequest() ;
      }

      return result ;
    }
  }
}
