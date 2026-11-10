using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class BrokerAccountServiceTests
{
  private readonly IRepository<BrokerAccount> _accountRepo = Substitute.For<IRepository<BrokerAccount>>() ;

  private readonly IRepository<BrokerAccountMember> _memberRepo =
    Substitute.For<IRepository<BrokerAccountMember>>() ;

  private readonly IRepository<SpendAccount> _spendRepo = Substitute.For<IRepository<SpendAccount>>() ;

  private readonly IRepository<AppUser> _userRepo = Substitute.For<IRepository<AppUser>>() ;

  private BrokerAccountService CreateSut() => new(_accountRepo, _memberRepo, _userRepo, _spendRepo) ;

  private void SetupSpendAccount( long spendId, long[] memberIds )
  {
    var account = new SpendAccount
    {
      Id = spendId,
      Name = $"Spend{spendId}"
    } ;
    foreach ( var memberId in memberIds )
    {
      account.Members.Add( new SpendAccountMember
      {
        SpendAccountId = spendId,
        AppUserId = memberId,
        AppUser = BuildUser( memberId ),
        JoinedAt = DateTime.UtcNow
      } ) ;
    }

    _spendRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
  }

  private void SetupNoSpendAccounts() => _spendRepo.Query().Returns( new List<SpendAccount>().BuildMock() ) ;

  private static AppUser BuildUser( long id, string email = "", bool isDeleted = false ) =>
    new()
    {
      Id = id,
      Email = string.IsNullOrEmpty( email ) ? $"user{id}@example.com" : email,
      GoogleSub = $"g-{id}",
      IsDeleted = isDeleted
    } ;

  private static BrokerAccountMember BuildMember( long accountId, AppUser user ) =>
    new()
    {
      BrokerAccountId = accountId,
      AppUserId = user.Id,
      AppUser = user,
      JoinedAt = DateTime.UtcNow
    } ;

  private static BrokerAccount BuildAccount(
    long id,
    long[] memberIds,
    string name = "VPS",
    bool isDeleted = false,
    long spendAccountId = 1 )
  {
    var account = new BrokerAccount
    {
      Id = id,
      Name = name,
      SpendAccountId = spendAccountId,
      IsDeleted = isDeleted
    } ;
    foreach ( var memberId in memberIds )
    {
      account.Members.Add( BuildMember( id, BuildUser( memberId ) ) ) ;
    }

    return account ;
  }

  [Fact]
  public async Task CreateAsync_AddsCreatorAsFirstMember_AndSaves()
  {
    SetupSpendAccount( 7, [42] ) ;
    var sut = CreateSut() ;
    var account = new BrokerAccount
    {
      Name = "VPS",
      SpendAccountId = 7
    } ;

    var result = await sut.CreateAsync( account, 42 ) ;

    result.Should().BeSameAs( account ) ;
    account.Members.Should().ContainSingle() ;
    account.Members.First().AppUserId.Should().Be( 42 ) ;
    await _accountRepo.Received( 1 ).AddAsync( account ) ;
    await _accountRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task CreateAsync_ThrowsKeyNotFound_WhenCallerIsNotMemberOfSpendAccount()
  {
    SetupSpendAccount( 7, [99] ) ;
    var sut = CreateSut() ;
    var account = new BrokerAccount
    {
      Name = "VPS",
      SpendAccountId = 7
    } ;

    var act = async () => await sut.CreateAsync( account, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    await _accountRepo.DidNotReceive().AddAsync( Arg.Any<BrokerAccount>() ) ;
  }

  [Fact]
  public async Task CreateAsync_ThrowsKeyNotFound_WhenSpendAccountIsMissingOrSoftDeleted()
  {
    SetupNoSpendAccounts() ;
    var sut = CreateSut() ;
    var account = new BrokerAccount
    {
      Name = "VPS",
      SpendAccountId = 7
    } ;

    var act = async () => await sut.CreateAsync( account, 42 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    await _accountRepo.DidNotReceive().AddAsync( Arg.Any<BrokerAccount>() ) ;
  }

  [Fact]
  public async Task GetAllForUserAsync_FiltersByMembership()
  {
    List<BrokerAccount> accounts =
    [
      BuildAccount( 1, [10] ),
      BuildAccount( 2, [10, 20], "TCBS" ),
      BuildAccount( 3, [20], "OtherUserBroker" )
    ] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 10, new PaginationQuery() ) ;

    result.Items.Should().HaveCount( 2 ) ;
    result.TotalCount.Should().Be( 2L ) ;
    result.Items.Select( a => a.Id ).Should().BeEquivalentTo( [1L, 2L] ) ;
  }

  [Fact]
  public async Task GetAllForUserAsync_OrdersByName()
  {
    List<BrokerAccount> accounts =
    [
      BuildAccount( 1, [10], "Zenith" ),
      BuildAccount( 2, [10], "Alpha" ),
      BuildAccount( 3, [10], "Midway" )
    ] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 10, new PaginationQuery() ) ;

    result.Items.Select( a => a.Name ).Should().ContainInOrder( "Alpha", "Midway", "Zenith" ) ;
  }

  [Fact]
  public async Task GetByIdForUserAsync_ReturnsAccount_WhenCallerIsMember()
  {
    List<BrokerAccount> accounts = [BuildAccount( 5, [10, 20] )] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdForUserAsync( 5, 20 ) ;

    result.Should().NotBeNull() ;
    result!.Id.Should().Be( 5 ) ;
  }

  [Fact]
  public async Task GetByIdForUserAsync_ReturnsNull_WhenCallerIsNotMember()
  {
    List<BrokerAccount> accounts = [BuildAccount( 5, [99] )] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdForUserAsync( 5, 10 ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task ExistsForUserAsync_True_WhenMember()
  {
    List<BrokerAccount> accounts = [BuildAccount( 5, [10] )] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsForUserAsync( 5, 10 ) ).Should().BeTrue() ;
  }

  [Fact]
  public async Task ExistsForUserAsync_False_WhenNotMember()
  {
    List<BrokerAccount> accounts = [BuildAccount( 5, [99] )] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsForUserAsync( 5, 10 ) ).Should().BeFalse() ;
  }

  [Fact]
  public async Task UpdateAsync_ReturnsNull_WhenCallerIsNotMember()
  {
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      BuildAccount( 5, [99], spendAccountId: 7 )
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 5, 10, "Renamed", 7 ) ;

    result.Should().BeNull() ;
    await _accountRepo.DidNotReceive().SaveAsync() ;
  }

  [Fact]
  public async Task UpdateAsync_DoesNotRevalidateSpendMembership_WhenSpendAccountIdUnchanged()
  {
    var account = BuildAccount( 5, [10], spendAccountId: 7 ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 5, 10, "Renamed", 7 ) ;

    result.Should().BeSameAs( account ) ;
    account.Name.Should().Be( "Renamed" ) ;
    account.SpendAccountId.Should().Be( 7 ) ;
    await _accountRepo.Received( 1 ).SaveAsync() ;
    _spendRepo.DidNotReceive().Query() ;
  }

  [Fact]
  public async Task UpdateAsync_RevalidatesSpendMembership_AndRefetches_WhenSpendAccountIdChanges()
  {
    var account = BuildAccount( 5, [10], spendAccountId: 7 ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    SetupSpendAccount( 9, [10] ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 5, 10, "VPS", 9 ) ;

    result.Should().BeSameAs( account ) ;
    account.SpendAccountId.Should().Be( 9 ) ;
    await _accountRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task UpdateAsync_ThrowsKeyNotFound_WhenChangedSpendAccountIsNotMember()
  {
    var account = BuildAccount( 5, [10], spendAccountId: 7 ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    SetupSpendAccount( 9, [99] ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.UpdateAsync( 5, 10, "VPS", 9 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    await _accountRepo.DidNotReceive().SaveAsync() ;
  }

  [Fact]
  public async Task AdminUpdateAsync_ReturnsNull_WhenMissing()
  {
    _accountRepo.Query().Returns( new List<BrokerAccount>().BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 99, "VPS", 7 ) ;

    result.Should().BeNull() ;
    await _accountRepo.DidNotReceive().SaveAsync() ;
  }

  [Fact]
  public async Task AdminUpdateAsync_RenameOnly_SkipsSpendAccountLookup_AndSaves()
  {
    var account = BuildAccount( 5, [10], spendAccountId: 7 ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 5, "Renamed", 7 ) ;

    result.Should().BeSameAs( account ) ;
    account.Name.Should().Be( "Renamed" ) ;
    await _spendRepo.DidNotReceive().GetByIdOrThrowAsync( Arg.Any<long>() ) ;
    await _accountRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task AdminUpdateAsync_PropagatesKeyNotFound_WhenSpendAccountMissing()
  {
    var account = BuildAccount( 5, [10], spendAccountId: 7 ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    _spendRepo.GetByIdOrThrowAsync( 9L )
      .Returns<Task<SpendAccount>>( _ => throw new KeyNotFoundException() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.UpdateAsync( 5, "Renamed", 9 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    await _accountRepo.DidNotReceive().SaveAsync() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_SoftDeletes_WhenCallerIsMember()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 5, 10 ) ;

    _accountRepo.Received( 1 ).SoftDelete( account ) ;
    await _accountRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_ThrowsKeyNotFound_WhenCallerIsNotMember()
  {
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      BuildAccount( 5, [99] )
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.SoftDeleteAsync( 5, 10 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _accountRepo.DidNotReceive().SoftDelete( Arg.Any<BrokerAccount>() ) ;
  }

  [Fact]
  public async Task GetMembersAsync_ReturnsMembersOrderedByJoinedAt_WhenCallerIsMember()
  {
    var account = BuildAccount( 5, [10, 20] ) ;
    var members = account.Members.ToList() ;
    members[ 0 ].JoinedAt = new DateTime( 2024, 6, 1, 0, 0, 0, DateTimeKind.Utc ) ;
    members[ 1 ].JoinedAt = new DateTime( 2024, 1, 1, 0, 0, 0, DateTimeKind.Utc ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    _memberRepo.Query().Returns( members.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetMembersAsync( 5, 10, new PaginationQuery() ) ;

    result.Items.Select( m => m.AppUserId ).Should().ContainInOrder( 20L, 10L ) ;
  }

  // docs/rules/access-control.md: GetMembersAsync bakes the caller-membership check into the query that
  // fetches the members, so there is no separate precondition round-trip against the member table.
  [Fact]
  public async Task GetMembersAsync_ChecksMembershipInTheSameQuery_NoSeparateMemberQuery()
  {
    var account = BuildAccount( 5, [10, 20] ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    _memberRepo.Query().Returns( account.Members.ToList().BuildMock() ) ;
    _accountRepo.ClearReceivedCalls() ;
    _memberRepo.ClearReceivedCalls() ;
    var sut = CreateSut() ;

    var result = await sut.GetMembersAsync( 5, 10, new PaginationQuery() ) ;

    result.Items.Select( m => m.AppUserId ).Should().BeEquivalentTo( [10L, 20L] ) ;
    _memberRepo.DidNotReceive().Query() ;
  }

  [Fact]
  public async Task GetMembersAsync_Throws_WhenCallerIsNotMember()
  {
    _accountRepo.Query().Returns( new List<BrokerAccount>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.GetMembersAsync( 5, 10, new PaginationQuery() ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task AddMemberAsync_AddsNewMember_WhenTargetExists()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    _userRepo.Query().Returns( new List<AppUser>
    {
      BuildUser( 20, "new@example.com" )
    }.BuildMock() ) ;
    _memberRepo.Query().Returns( account.Members.ToList().BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.AddMemberAsync( 5, 10, "new@example.com" ) ;

    await _memberRepo.Received( 1 ).AddAsync( Arg.Is<BrokerAccountMember>( m =>
      m.BrokerAccountId == 5 && m.AppUserId == 20 ) ) ;
    await _memberRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task AddMemberAsync_NormalizesEmailBeforeLookup()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    _userRepo.Query().Returns( new List<AppUser>
    {
      BuildUser( 20, "lower@example.com" )
    }.BuildMock() ) ;
    _memberRepo.Query().Returns( new List<BrokerAccountMember>().BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.AddMemberAsync( 5, 10, "  LOWER@Example.COM  " ) ;

    await _memberRepo.Received( 1 ).AddAsync( Arg.Is<BrokerAccountMember>( m => m.AppUserId == 20 ) ) ;
  }

  [Fact]
  public async Task AddMemberAsync_IsIdempotent_WhenTargetIsAlreadyMember()
  {
    var account = BuildAccount( 5, [10, 20] ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    _userRepo.Query().Returns( new List<AppUser>
    {
      BuildUser( 20, "existing@example.com" )
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.AddMemberAsync( 5, 10, "existing@example.com" ) ;

    await _memberRepo.DidNotReceive().AddAsync( Arg.Any<BrokerAccountMember>() ) ;
    await _memberRepo.DidNotReceive().SaveAsync() ;
    result.Select( m => m.AppUserId ).Should().BeEquivalentTo( [10L, 20L] ) ;
  }

  [Fact]
  public async Task AddMemberAsync_ThrowsKeyNotFound_WhenTargetEmailIsUnknown()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    _userRepo.Query().Returns( new List<AppUser>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.AddMemberAsync( 5, 10, "ghost@example.com" ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    await _memberRepo.DidNotReceive().AddAsync( Arg.Any<BrokerAccountMember>() ) ;
  }

  [Fact]
  public async Task AddMemberAsync_ThrowsKeyNotFound_WhenCallerIsNotMember()
  {
    _accountRepo.Query().Returns( new List<BrokerAccount>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.AddMemberAsync( 5, 10, "anyone@example.com" ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _userRepo.DidNotReceive().Query() ;
  }

  [Fact]
  public async Task RemoveMemberAsync_RemovesMember_WhenMultipleRemain()
  {
    var account = BuildAccount( 5, [10, 20, 30] ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.RemoveMemberAsync( 5, 10, 20 ) ;

    _memberRepo.Received( 1 ).Delete( Arg.Is<BrokerAccountMember>( m =>
      m.BrokerAccountId == 5 && m.AppUserId == 20 ) ) ;
    await _memberRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task RemoveMemberAsync_ThrowsBadRequest_WhenRemovingLastMember()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.RemoveMemberAsync( 5, 10, 10 ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
    _memberRepo.DidNotReceive().Delete( Arg.Any<BrokerAccountMember>() ) ;
  }

  [Fact]
  public async Task RemoveMemberAsync_ThrowsKeyNotFound_WhenTargetIsNotMember()
  {
    var account = BuildAccount( 5, [10, 20] ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.RemoveMemberAsync( 5, 10, 99 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _memberRepo.DidNotReceive().Delete( Arg.Any<BrokerAccountMember>() ) ;
  }

  [Fact]
  public async Task RemoveMemberAsync_ThrowsKeyNotFound_WhenCallerIsNotMember()
  {
    _accountRepo.Query().Returns( new List<BrokerAccount>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.RemoveMemberAsync( 5, 10, 20 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task GetAllAsync_NoFilter_ReturnsAllAccountsOrderedByName()
  {
    List<BrokerAccount> accounts =
    [
      BuildAccount( 1, [20], "Zebra" ),
      BuildAccount( 2, [10], "Bravo" ),
      BuildAccount( 3, [10], "Alpha" )
    ] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery() ) ;

    result.Items.Should().HaveCount( 3 ) ;
    result.TotalCount.Should().Be( 3L ) ;
    result.Items.Select( a => a.Name ).Should().ContainInOrder( "Alpha", "Bravo", "Zebra" ) ;
  }

  [Fact]
  public async Task GetAllAsync_WithUserIdFilter_ReturnsOnlyAccountsUserIsMemberOf()
  {
    List<BrokerAccount> accounts =
    [
      BuildAccount( 1, [20], "Zebra" ),
      BuildAccount( 2, [10], "Bravo" ),
      BuildAccount( 3, [10, 20], "Alpha" )
    ] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery(), 10 ) ;

    result.Items.Select( a => a.Id ).Should().BeEquivalentTo( [2L, 3L] ) ;
  }

  [Fact]
  public async Task GetByIdAsync_ReturnsAccount_RegardlessOfMembership()
  {
    var account = BuildAccount( 5, [99] ) ;
    _accountRepo.Query().Returns( new List<BrokerAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 5 ) ;

    result.Should().NotBeNull() ;
    result!.Id.Should().Be( 5 ) ;
  }

  [Fact]
  public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
  {
    _accountRepo.Query().Returns( new List<BrokerAccount>().BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 5 ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task AdminSoftDeleteAsync_SoftDeletes_WhenFound()
  {
    var account = BuildAccount( 5, [99] ) ;
    _accountRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 5 ) ;

    _accountRepo.Received( 1 ).SoftDelete( account ) ;
    await _accountRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task AdminSoftDeleteAsync_Propagates_KeyNotFoundException_FromRepository()
  {
    _accountRepo.GetByIdOrThrowAsync( 99L )
      .Returns<Task<BrokerAccount>>( _ => throw new KeyNotFoundException() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.SoftDeleteAsync( 99 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _accountRepo.DidNotReceive().SoftDelete( Arg.Any<BrokerAccount>() ) ;
  }
}
