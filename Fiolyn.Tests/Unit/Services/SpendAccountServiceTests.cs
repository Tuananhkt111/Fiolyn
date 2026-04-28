using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class SpendAccountServiceTests
{
  private readonly IRepository<SpendAccount> _accountRepo = Substitute.For<IRepository<SpendAccount>>() ;
  private readonly IRepository<BrokerAccount> _brokerRepo = Substitute.For<IRepository<BrokerAccount>>() ;
  private readonly IRepository<SpendAccountMember> _memberRepo = Substitute.For<IRepository<SpendAccountMember>>() ;
  private readonly IRepository<AppUser> _userRepo = Substitute.For<IRepository<AppUser>>() ;

  private SpendAccountService CreateSut()
  {
    _brokerRepo.Query().Returns( new List<BrokerAccount>().BuildMock() ) ;
    return new SpendAccountService( _accountRepo, _memberRepo, _userRepo, _brokerRepo ) ;
  }

  private void SetupBrokerReferencingSpend( long spendAccountId ) =>
    _brokerRepo.Query().Returns( new List<BrokerAccount>
    {
      new()
      {
        Id = 100,
        Name = "VPS",
        SpendAccountId = spendAccountId
      }
    }.BuildMock() ) ;

  private static AppUser BuildUser( long id, string email = "", bool isDeleted = false ) =>
    new()
    {
      Id = id,
      Email = string.IsNullOrEmpty( email ) ? $"user{id}@example.com" : email,
      GoogleSub = $"g-{id}",
      IsDeleted = isDeleted
    } ;

  private static SpendAccountMember BuildMember( long accountId, AppUser user ) =>
    new()
    {
      SpendAccountId = accountId,
      AppUserId = user.Id,
      AppUser = user,
      JoinedAt = DateTime.UtcNow
    } ;

  private static SpendAccount BuildAccount(
    long id,
    long[] memberIds,
    string name = "Wallet",
    SpendAccountType type = SpendAccountType.Cash,
    long balance = 0L,
    bool isDeleted = false )
  {
    var account = new SpendAccount
    {
      Id = id,
      Name = name,
      Type = type,
      Balance = balance,
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
    var sut = CreateSut() ;
    var account = new SpendAccount
    {
      Name = "Wallet",
      Type = SpendAccountType.Cash,
      Balance = 1_500_000L
    } ;

    var result = await sut.CreateAsync( account, 42 ) ;

    result.Should().BeSameAs( account ) ;
    account.Members.Should().ContainSingle() ;
    account.Members.First().AppUserId.Should().Be( 42 ) ;
    await _accountRepo.Received( 1 ).AddAsync( account ) ;
    await _accountRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task GetAllForUserAsync_FiltersByMembership()
  {
    List<SpendAccount> accounts =
    [
      BuildAccount( 1, [10] ),
      BuildAccount( 2, [10, 20], "Visa" ),
      BuildAccount( 3, [20], "OtherUserWallet" )
    ] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 10 ) ;

    result.Should().HaveCount( 2 ) ;
    result.Select( a => a.Id ).Should().BeEquivalentTo( [1L, 2L] ) ;
  }

  [Fact]
  public async Task GetAllForUserAsync_OrdersByName()
  {
    List<SpendAccount> accounts =
    [
      BuildAccount( 1, [10], "Zenith" ),
      BuildAccount( 2, [10], "Alpha" ),
      BuildAccount( 3, [10], "Midway" )
    ] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 10 ) ;

    result.Select( a => a.Name ).Should().ContainInOrder( "Alpha", "Midway", "Zenith" ) ;
  }

  [Fact]
  public async Task GetByIdForUserAsync_ReturnsAccount_WhenCallerIsMember()
  {
    List<SpendAccount> accounts = [BuildAccount( 5, [10, 20] )] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdForUserAsync( 5, 20 ) ;

    result.Should().NotBeNull() ;
    result!.Id.Should().Be( 5 ) ;
  }

  [Fact]
  public async Task GetByIdForUserAsync_ReturnsNull_WhenCallerIsNotMember()
  {
    List<SpendAccount> accounts = [BuildAccount( 5, [99] )] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdForUserAsync( 5, 10 ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task UpdateAsync_DoesNotCallRepoUpdate_AndSaves()
  {
    // Update relies on EF's per-property change detection on the already-tracked entity, so calling
    // repository.Update (which marks every column modified) would clobber concurrent atomic Balance
    // writes — the service must not invoke it.
    var sut = CreateSut() ;
    var account = BuildAccount( 5, [10], "Wallet", SpendAccountType.Cash, 2_000_000L ) ;

    await sut.UpdateAsync( account ) ;

    _accountRepo.DidNotReceive().Update( Arg.Any<SpendAccount>() ) ;
    await _accountRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task AdjustBalanceForUserAsync_AppliesDelta_WhenCallerIsMember()
  {
    var account = BuildAccount( 5, [10], balance: 1_000_000L ) ;
    _accountRepo.Query().Returns( _ => new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _accountRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    var sut = CreateSut() ;

    var newBalance = await sut.AdjustBalanceForUserAsync( 5, 10, 250_000L ) ;

    newBalance.Should().Be( 1_250_000L ) ;
    await _accountRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task AdjustBalanceForUserAsync_Throws_WhenCallerIsNotMember()
  {
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      BuildAccount( 5, [99] )
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.AdjustBalanceForUserAsync( 5, 10, 100L ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    await _accountRepo.DidNotReceive().GetByIdOrThrowAsync( Arg.Any<object>() ) ;
  }

  [Fact]
  public async Task AdjustBalanceAsync_AdminPath_AppliesDelta_WithoutMembershipCheck()
  {
    var account = BuildAccount( 5, [99], balance: 500_000L ) ;
    _accountRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    var sut = CreateSut() ;

    var newBalance = await sut.AdjustBalanceAsync( 5, -200_000L ) ;

    newBalance.Should().Be( 300_000L ) ;
    await _accountRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_SoftDeletes_WhenCallerIsMember()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
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
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      BuildAccount( 5, [99] )
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.SoftDeleteAsync( 5, 10 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _accountRepo.DidNotReceive().SoftDelete( Arg.Any<SpendAccount>() ) ;
  }

  [Fact]
  public async Task SoftDeleteAsync_ThrowsBadRequest_WhenLinkedFromActiveBroker()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;
    SetupBrokerReferencingSpend( 5 ) ;

    var act = async () => await sut.SoftDeleteAsync( 5, 10 ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
    _accountRepo.DidNotReceive().SoftDelete( Arg.Any<SpendAccount>() ) ;
  }

  [Fact]
  public async Task AdminSoftDeleteAsync_ThrowsBadRequest_WhenLinkedFromActiveBroker()
  {
    var account = BuildAccount( 5, [99] ) ;
    _accountRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    var sut = CreateSut() ;
    SetupBrokerReferencingSpend( 5 ) ;

    var act = async () => await sut.SoftDeleteAsync( 5 ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
    _accountRepo.DidNotReceive().SoftDelete( Arg.Any<SpendAccount>() ) ;
  }

  [Fact]
  public async Task SoftDeleteAsync_SecondCall_ThrowsKeyNotFound_WhenAlreadyDeleted()
  {
    // The global query filter excludes soft-deleted accounts, so the second caller sees an empty
    // result and gets the same 404 a non-member would.
    _accountRepo.Query().Returns( new List<SpendAccount>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.SoftDeleteAsync( 5, 10 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _accountRepo.DidNotReceive().SoftDelete( Arg.Any<SpendAccount>() ) ;
  }

  [Fact]
  public async Task GetMembersAsync_ReturnsMembersOrderedByJoinedAt_WhenCallerIsMember()
  {
    var account = BuildAccount( 5, [10, 20] ) ;
    var members = account.Members.ToList() ;
    members[ 0 ].JoinedAt = new DateTime( 2024, 6, 1, 0, 0, 0, DateTimeKind.Utc ) ;
    members[ 1 ].JoinedAt = new DateTime( 2024, 1, 1, 0, 0, 0, DateTimeKind.Utc ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetMembersAsync( 5, 10 ) ;

    result.Select( m => m.AppUserId ).Should().ContainInOrder( 20L, 10L ) ;
  }

  [Fact]
  public async Task GetMembersAsync_Throws_WhenCallerIsNotMember()
  {
    _accountRepo.Query().Returns( new List<SpendAccount>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.GetMembersAsync( 5, 10 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task AddMemberAsync_AddsNewMember_WhenTargetExists()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
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

    await _memberRepo.Received( 1 ).AddAsync( Arg.Is<SpendAccountMember>( m =>
      m.SpendAccountId == 5 && m.AppUserId == 20 ) ) ;
    await _memberRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task AddMemberAsync_NormalizesEmailBeforeLookup()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _userRepo.Query().Returns( new List<AppUser>
    {
      BuildUser( 20, "lower@example.com" )
    }.BuildMock() ) ;
    _memberRepo.Query().Returns( new List<SpendAccountMember>().BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.AddMemberAsync( 5, 10, "  LOWER@Example.COM  " ) ;

    await _memberRepo.Received( 1 ).AddAsync( Arg.Is<SpendAccountMember>( m => m.AppUserId == 20 ) ) ;
  }

  [Fact]
  public async Task AddMemberAsync_IsIdempotent_WhenTargetIsAlreadyMember()
  {
    var account = BuildAccount( 5, [10, 20] ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _userRepo.Query().Returns( new List<AppUser>
    {
      BuildUser( 20, "existing@example.com" )
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.AddMemberAsync( 5, 10, "existing@example.com" ) ;

    await _memberRepo.DidNotReceive().AddAsync( Arg.Any<SpendAccountMember>() ) ;
    await _memberRepo.DidNotReceive().SaveAsync() ;
    result.Select( m => m.AppUserId ).Should().BeEquivalentTo( [10L, 20L] ) ;
  }

  [Fact]
  public async Task AddMemberAsync_ThrowsKeyNotFound_WhenTargetEmailIsUnknown()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _userRepo.Query().Returns( new List<AppUser>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.AddMemberAsync( 5, 10, "ghost@example.com" ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    await _memberRepo.DidNotReceive().AddAsync( Arg.Any<SpendAccountMember>() ) ;
  }

  [Fact]
  public async Task AddMemberAsync_ThrowsKeyNotFound_WhenCallerIsNotMember()
  {
    _accountRepo.Query().Returns( new List<SpendAccount>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.AddMemberAsync( 5, 10, "anyone@example.com" ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _userRepo.DidNotReceive().Query() ;
  }

  [Fact]
  public async Task RemoveMemberAsync_RemovesMember_WhenMultipleRemain()
  {
    var account = BuildAccount( 5, [10, 20, 30] ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.RemoveMemberAsync( 5, 10, 20 ) ;

    _memberRepo.Received( 1 ).Delete( Arg.Is<SpendAccountMember>( m =>
      m.SpendAccountId == 5 && m.AppUserId == 20 ) ) ;
    await _memberRepo.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task RemoveMemberAsync_ThrowsBadRequest_WhenRemovingLastMember()
  {
    var account = BuildAccount( 5, [10] ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.RemoveMemberAsync( 5, 10, 10 ) ;

    await act.Should().ThrowAsync<BadRequestException>() ;
    _memberRepo.DidNotReceive().Delete( Arg.Any<SpendAccountMember>() ) ;
  }

  [Fact]
  public async Task RemoveMemberAsync_ThrowsKeyNotFound_WhenTargetIsNotMember()
  {
    var account = BuildAccount( 5, [10, 20] ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.RemoveMemberAsync( 5, 10, 99 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _memberRepo.DidNotReceive().Delete( Arg.Any<SpendAccountMember>() ) ;
  }

  [Fact]
  public async Task RemoveMemberAsync_ThrowsKeyNotFound_WhenCallerIsNotMember()
  {
    _accountRepo.Query().Returns( new List<SpendAccount>().BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.RemoveMemberAsync( 5, 10, 20 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
  }

  [Fact]
  public async Task GetAllAsync_NoFilter_ReturnsAllAccountsOrderedByName()
  {
    List<SpendAccount> accounts =
    [
      BuildAccount( 1, [20], "Zebra" ),
      BuildAccount( 2, [10], "Bravo" ),
      BuildAccount( 3, [10], "Alpha" )
    ] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    result.Should().HaveCount( 3 ) ;
    result.Select( a => a.Name ).Should().ContainInOrder( "Alpha", "Bravo", "Zebra" ) ;
  }

  [Fact]
  public async Task GetAllAsync_WithUserIdFilter_ReturnsOnlyAccountsUserIsMemberOf()
  {
    List<SpendAccount> accounts =
    [
      BuildAccount( 1, [20], "Zebra" ),
      BuildAccount( 2, [10], "Bravo" ),
      BuildAccount( 3, [10, 20], "Alpha" )
    ] ;
    _accountRepo.Query().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( 10 ) ;

    result.Select( a => a.Id ).Should().BeEquivalentTo( [2L, 3L] ) ;
  }

  [Fact]
  public async Task GetByIdAsync_ReturnsAccount_RegardlessOfMembership()
  {
    var account = BuildAccount( 5, [99] ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
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
    _accountRepo.Query().Returns( new List<SpendAccount>().BuildMock() ) ;
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
      .Returns<Task<SpendAccount>>( _ => throw new KeyNotFoundException() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.SoftDeleteAsync( 99 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _accountRepo.DidNotReceive().SoftDelete( Arg.Any<SpendAccount>() ) ;
  }

  [Fact]
  public async Task AdjustBalanceForUserAsync_ThrowsConflict_WhenBalanceConcurrentlyModified()
  {
    var account = BuildAccount( 5, [10], balance: 1_000_000L ) ;
    _accountRepo.Query().Returns( new List<SpendAccount>
    {
      account
    }.BuildMock() ) ;
    _accountRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    _accountRepo.SaveAsync().Returns( Task.FromException( new DbUpdateConcurrencyException() ) ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.AdjustBalanceForUserAsync( 5, 10, 250_000L ) ;

    await act.Should().ThrowAsync<ConflictException>() ;
  }

  [Fact]
  public async Task AdjustBalanceAsync_ThrowsConflict_WhenBalanceConcurrentlyModified()
  {
    var account = BuildAccount( 5, [99], balance: 500_000L ) ;
    _accountRepo.GetByIdOrThrowAsync( 5L ).Returns( account ) ;
    _accountRepo.SaveAsync().Returns( Task.FromException( new DbUpdateConcurrencyException() ) ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.AdjustBalanceAsync( 5, -200_000L ) ;

    await act.Should().ThrowAsync<ConflictException>() ;
  }
}
