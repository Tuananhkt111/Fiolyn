using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using Fiolyn.Services.Internal.BaseServices ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

/// <summary>
///   Behaviour of <c>CounterpartyResolver</c> (docs/reference/counterparties.md, docs/rules/counterparty.md).
///   The resolver is <c>internal</c> and the test project has no access to it, so it is exercised through
///   its two public callers: <see cref="LendTransactionService" /> (one name, via
///   <c>CounterpartyTransactionServiceBase</c>) and <see cref="IncomeTransactionService" /> (a batch of
///   names, via the share list). Reuse and staging on create are also smoke-tested in
///   <see cref="LendTransactionServiceTests" />; this class adds the update short-circuit, repointing,
///   the "stage, never save" contract and the one-round-trip batch lookup.
/// </summary>
public class CounterpartyResolverTests
{
  private const long UserId = 42 ;
  private const long SpendId = 5 ;

  private readonly IRepository<BorrowingTransaction> _borrowingRepo =
    Substitute.For<IRepository<BorrowingTransaction>>() ;

  private readonly IRepository<TransactionCategory> _categoryRepo =
    Substitute.For<IRepository<TransactionCategory>>() ;

  private readonly IRepository<Counterparty> _counterpartyRepo =
    Substitute.For<IRepository<Counterparty>>() ;

  private readonly IRepository<IncomeTransaction> _incomeRepo =
    Substitute.For<IRepository<IncomeTransaction>>() ;

  private readonly IRepository<LendTransaction> _lendRepo = Substitute.For<IRepository<LendTransaction>>() ;
  private readonly IRepository<SpendAccount> _spendRepo = Substitute.For<IRepository<SpendAccount>>() ;

  public CounterpartyResolverTests()
  {
    _categoryRepo.Query().Returns( new List<TransactionCategory>().BuildMock() ) ;
    var account = new SpendAccount
    {
      Id = SpendId,
      Members =
      [
        new SpendAccountMember
        {
          SpendAccountId = SpendId,
          AppUserId = UserId,
          AppUser = new AppUser
          {
            Id = UserId,
            Email = "u42@x.com",
            GoogleSub = "g42"
          },
          JoinedAt = DateTime.UtcNow
        }
      ]
    } ;
    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( SpendId ).Returns( account ) ;
  }

  private LendTransactionService CreateLendSut() => new(_lendRepo, _spendRepo, _categoryRepo, _counterpartyRepo) ;

  private IncomeTransactionService CreateIncomeSut() =>
    new(_incomeRepo, _spendRepo, _categoryRepo, _counterpartyRepo, _borrowingRepo) ;

  private static Counterparty Contact( long id, string name, long ownerId = UserId ) =>
    new()
    {
      Id = id,
      AppUserId = ownerId,
      Name = name
    } ;

  private void SetupContacts( params Counterparty[] rows ) =>
    _counterpartyRepo.Query().Returns( rows.ToList().BuildMock() ) ;

  /// <summary>Records every counterparty handed to <c>AddAsync</c> (the resolver's staging call).</summary>
  private async Task<List<Counterparty>> CaptureStagedAsync()
  {
    var staged = new List<Counterparty>() ;
    await _counterpartyRepo.AddAsync( Arg.Do<Counterparty>( c => staged.Add( c ) ) ) ;
    return staged ;
  }

  /// <summary>An existing lend, as the controller would hand it to UpdateAsync: tracked, nav loaded.</summary>
  private LendTransaction ExistingLend( Counterparty current )
  {
    var lend = new LendTransaction
    {
      Id = 1,
      SpendAccountId = SpendId,
      Amount = -100,
      Counterparty = current,
      CounterpartyId = current.Id
    } ;
    _lendRepo.Query().Returns( new List<LendTransaction>
    {
      lend
    }.BuildMock() ) ;
    return lend ;
  }

  // ---------------------------------------------------------------- create: match, then stage

  [Theory]
  [InlineData( " Bob" )]
  [InlineData( "bob " )]
  [InlineData( "  BOB  " )]
  public async Task CreateAsync_PaddedOrDifferentlyCasedName_ReusesExistingContact( string requested )
  {
    var bob = Contact( 11, "Bob" ) ;
    SetupContacts( bob ) ;
    var staged = await CaptureStagedAsync() ;
    var sut = CreateLendSut() ;
    var lend = new LendTransaction
    {
      SpendAccountId = SpendId,
      Amount = -100,
      Date = DateTime.UtcNow
    } ;

    var result = await sut.CreateAsync( lend, UserId, requested ) ;

    result.Counterparty.Should().BeSameAs( bob ) ;
    staged.Should().BeEmpty() ;
  }

  [Fact]
  public async Task CreateAsync_NameOnlyAnotherUserHas_StagesANewContactForTheCaller()
  {
    // The contact list is private per user: someone else's "Bob" is not a match.
    var othersBob = Contact( 11, "Bob", ownerId: 99 ) ;
    SetupContacts( othersBob ) ;
    var staged = await CaptureStagedAsync() ;
    var sut = CreateLendSut() ;
    var lend = new LendTransaction
    {
      SpendAccountId = SpendId,
      Amount = -100,
      Date = DateTime.UtcNow
    } ;

    var result = await sut.CreateAsync( lend, UserId, "Bob" ) ;

    var mine = staged.Should().ContainSingle().Subject ;
    mine.AppUserId.Should().Be( UserId ) ;
    mine.Name.Should().Be( "Bob" ) ;
    result.Counterparty.Should().BeSameAs( mine ).And.NotBeSameAs( othersBob ) ;
  }

  [Fact]
  public async Task CreateAsync_NewName_StagesContactWithoutSavingItOnItsOwn()
  {
    SetupContacts() ;
    var staged = await CaptureStagedAsync() ;
    var sut = CreateLendSut() ;
    var lend = new LendTransaction
    {
      SpendAccountId = SpendId,
      Amount = -100,
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( lend, UserId, "Alice" ) ;

    // The contact is staged for the leaf's single save, so a failed transaction write takes it with it.
    staged.Should().ContainSingle().Which.Name.Should().Be( "Alice" ) ;
    await _counterpartyRepo.DidNotReceive().SaveAsync() ;
    await _lendRepo.Received( 1 ).SaveAsync() ;
  }

  // ---------------------------------------------------------------- update: skip, repoint, never rename

  [Theory]
  [InlineData( "Bob" )]
  [InlineData( "bob" )]
  [InlineData( "  BOB  " )]
  public async Task UpdateAsync_UnchangedName_SkipsTheResolverLookupEntirely( string requested )
  {
    var bob = Contact( 11, "Bob" ) ;
    var lend = ExistingLend( bob ) ;
    _counterpartyRepo.ClearReceivedCalls() ;
    var sut = CreateLendSut() ;

    await sut.UpdateAsync( lend, UserId, requested ) ;

    _counterpartyRepo.ReceivedCalls().Should().BeEmpty( "a PUT that doesn't touch the counterparty must not pay for a lookup" ) ;
    lend.Counterparty.Should().BeSameAs( bob ) ;
    bob.Name.Should().Be( "Bob" ) ;
    await _lendRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task UpdateAsync_DifferentExistingName_RepointsAndRenamesNeitherContact()
  {
    var bob = Contact( 11, "Bob" ) ;
    var carol = Contact( 12, "Carol" ) ;
    SetupContacts( bob, carol ) ;
    var lend = ExistingLend( bob ) ;
    var sut = CreateLendSut() ;

    await sut.UpdateAsync( lend, UserId, "CAROL" ) ;

    lend.Counterparty.Should().BeSameAs( carol ) ;
    bob.Name.Should().Be( "Bob", "a different name repoints the row, it never renames the original contact" ) ;
    carol.Name.Should().Be( "Carol", "the resolved contact keeps its own spelling, not the request's" ) ;
  }

  [Fact]
  public async Task UpdateAsync_DifferentNewName_StagesContactAndAssignsTheNavNotTheScalarFk()
  {
    var bob = Contact( 11, "Bob" ) ;
    SetupContacts( bob ) ;
    var staged = await CaptureStagedAsync() ;
    var lend = ExistingLend( bob ) ;
    var sut = CreateLendSut() ;

    await sut.UpdateAsync( lend, UserId, "  Dave " ) ;

    var dave = staged.Should().ContainSingle().Subject ;
    dave.Name.Should().Be( "Dave" ) ;
    dave.AppUserId.Should().Be( UserId ) ;
    lend.Counterparty.Should().BeSameAs( dave ) ;
    // A staged contact has Id == 0 until the save, so the scalar FK must not be copied from it.
    lend.CounterpartyId.Should().Be( 11 ) ;
    bob.Name.Should().Be( "Bob" ) ;
  }

  [Fact]
  public async Task UpdateAsync_BlankName_ThrowsBadRequestAndDoesNotSave()
  {
    var bob = Contact( 11, "Bob" ) ;
    SetupContacts( bob ) ;
    var lend = ExistingLend( bob ) ;
    var sut = CreateLendSut() ;

    var act = () => sut.UpdateAsync( lend, UserId, "   " ) ;

    await act.Should().ThrowAsync<BadRequestException>().WithMessage( "Counterparty name is required." ) ;
    await _lendRepo.DidNotReceive().SaveAsync() ;
    lend.Counterparty.Should().BeSameAs( bob ) ;
  }

  // ---------------------------------------------------------------- batch: one round trip

  [Fact]
  public async Task CreateAsync_SeveralShareNames_ResolvesThemInOneQuery()
  {
    SetupContacts( Contact( 11, "Bob" ), Contact( 12, "Carol" ) ) ;
    _counterpartyRepo.ClearReceivedCalls() ;
    var sut = CreateIncomeSut() ;
    var income = new IncomeTransaction
    {
      SpendAccountId = SpendId,
      Amount = 1_000_000L,
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( income, UserId, [
      new TransactionShareInput( "Bob", 30 ),
      new TransactionShareInput( "Carol", 20 )
    ] ) ;

    _counterpartyRepo.ReceivedCalls().Count( c => c.GetMethodInfo().Name == nameof( IRepository<Counterparty>.Query ) )
      .Should().Be( 1, "the batch is one WHERE Name IN (...) lookup, not one query per name" ) ;
  }

  [Fact]
  public async Task CreateAsync_MixedExistingAndNewShareNames_ReusesHitsAndStagesOnlyTheMisses()
  {
    var bob = Contact( 11, "Bob" ) ;
    SetupContacts( bob ) ;
    var staged = await CaptureStagedAsync() ;
    var sut = CreateIncomeSut() ;
    var income = new IncomeTransaction
    {
      SpendAccountId = SpendId,
      Amount = 1_000_000L,
      Date = DateTime.UtcNow
    } ;

    await sut.CreateAsync( income, UserId, [
      new TransactionShareInput( "bob", 40 ),
      new TransactionShareInput( "  Dave ", 10 )
    ] ) ;

    var dave = staged.Should().ContainSingle().Subject ;
    dave.Name.Should().Be( "Dave" ) ;
    dave.AppUserId.Should().Be( UserId ) ;
    var children = income.Children.Cast<BorrowingTransaction>().ToList() ;
    children.Select( c => c.Counterparty ).Should().Equal( bob, dave ) ;
    // 40% and 10% of 1,000,000; the parent keeps the remaining 50%.
    children.Select( c => c.Amount ).Should().Equal( 400_000L, 100_000L ) ;
    income.Amount.Should().Be( 500_000L ) ;
    await _counterpartyRepo.DidNotReceive().SaveAsync() ;
  }
}
