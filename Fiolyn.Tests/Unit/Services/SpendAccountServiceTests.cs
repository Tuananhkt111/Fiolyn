using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class SpendAccountServiceTests
{
  private readonly IRepository<SpendAccount> _repository = Substitute.For<IRepository<SpendAccount>>() ;

  private SpendAccountService CreateSut() => new(_repository) ;

  private static SpendAccount BuildAccount( long id, long userId, string name = "Wallet",
    SpendAccountType type = SpendAccountType.Cash, long balance = 0L ) =>
    new()
    {
      Id = id,
      AppUserId = userId,
      Name = name,
      Type = type,
      Balance = balance
    } ;

  [Fact]
  public async Task GetAllForUserAsync_FiltersByUserId()
  {
    List<SpendAccount> accounts =
    [
      BuildAccount( 1, 10 ),
      BuildAccount( 2, 10, "Visa" ),
      BuildAccount( 3, 20, "OtherUserWallet" )
    ] ;
    _repository.QueryActive().Returns( accounts.BuildMock() ) ;
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
      BuildAccount( 1, 10, "Zenith" ),
      BuildAccount( 2, 10, "Alpha" ),
      BuildAccount( 3, 10, "Midway" )
    ] ;
    _repository.QueryActive().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 10 ) ;

    result.Select( a => a.Name ).Should().ContainInOrder( "Alpha", "Midway", "Zenith" ) ;
  }

  [Fact]
  public async Task GetByIdForUserAsync_ReturnsAccount_WhenOwnedByUser()
  {
    List<SpendAccount> accounts = [BuildAccount( 5, 10 )] ;
    _repository.QueryActive().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdForUserAsync( 5, 10 ) ;

    result.Should().NotBeNull() ;
    result!.Id.Should().Be( 5 ) ;
  }

  [Fact]
  public async Task GetByIdForUserAsync_ReturnsNull_WhenOwnedByAnotherUser()
  {
    List<SpendAccount> accounts = [BuildAccount( 5, 99 )] ;
    _repository.QueryActive().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdForUserAsync( 5, 10 ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task CreateAsync_SavesViaRepository()
  {
    var sut = CreateSut() ;
    var account = BuildAccount( 0, 10, "Wallet", SpendAccountType.Cash, 1_500_000L ) ;

    var result = await sut.CreateAsync( account ) ;

    result.Should().BeSameAs( account ) ;
    await _repository.Received( 1 ).AddAsync( account ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task UpdateAsync_UpdatesViaRepository()
  {
    var sut = CreateSut() ;
    var account = BuildAccount( 5, 10, "Wallet", SpendAccountType.Cash, 2_000_000L ) ;

    await sut.UpdateAsync( account ) ;

    _repository.Received( 1 ).Update( account ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_SoftDeletes_WhenOwnedByUser()
  {
    var account = BuildAccount( 5, 10 ) ;
    List<SpendAccount> accounts = [account] ;
    _repository.QueryActive().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 5, 10 ) ;

    _repository.Received( 1 ).SoftDelete( account ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_ThrowsKeyNotFound_WhenAccountDoesNotBelongToUser()
  {
    List<SpendAccount> accounts = [BuildAccount( 5, 99 )] ;
    _repository.QueryActive().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.SoftDeleteAsync( 5, 10 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _repository.DidNotReceive().SoftDelete( Arg.Any<SpendAccount>() ) ;
  }

  [Fact]
  public async Task GetAllAsync_NoFilter_ReturnsAllUsersAccountsOrderedByUserThenName()
  {
    List<SpendAccount> accounts =
    [
      BuildAccount( 1, 20, "Zebra" ),
      BuildAccount( 2, 10, "Bravo" ),
      BuildAccount( 3, 10, "Alpha" )
    ] ;
    _repository.QueryActive().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    result.Should().HaveCount( 3 ) ;
    result.Select( a => a.Id ).Should().ContainInOrder( 3L, 2L, 1L ) ;
  }

  [Fact]
  public async Task GetAllAsync_WithUserIdFilter_ReturnsOnlyThatUsersAccounts()
  {
    List<SpendAccount> accounts =
    [
      BuildAccount( 1, 20, "Zebra" ),
      BuildAccount( 2, 10, "Bravo" ),
      BuildAccount( 3, 10, "Alpha" )
    ] ;
    _repository.QueryActive().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( 10 ) ;

    result.Should().HaveCount( 2 ) ;
    result.Select( a => a.Id ).Should().ContainInOrder( 3L, 2L ) ;
  }

  [Fact]
  public async Task GetByIdAsync_ReturnsAccount_RegardlessOfOwner()
  {
    List<SpendAccount> accounts = [BuildAccount( 5, 99 )] ;
    _repository.QueryActive().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 5 ) ;

    result.Should().NotBeNull() ;
    result!.AppUserId.Should().Be( 99 ) ;
  }

  [Fact]
  public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
  {
    List<SpendAccount> accounts = [] ;
    _repository.QueryActive().Returns( accounts.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 5 ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task AdminSoftDeleteAsync_SoftDeletes_WhenFound()
  {
    var account = BuildAccount( 5, 99 ) ;
    _repository.GetActiveByIdOrThrowAsync( 5L ).Returns( account ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 5 ) ;

    _repository.Received( 1 ).SoftDelete( account ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task AdminSoftDeleteAsync_Propagates_KeyNotFoundException_FromRepository()
  {
    _repository.GetActiveByIdOrThrowAsync( 99L )
      .Returns<Task<SpendAccount>>( _ => throw new KeyNotFoundException() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.SoftDeleteAsync( 99 ) ;

    await act.Should().ThrowAsync<KeyNotFoundException>() ;
    _repository.DidNotReceive().SoftDelete( Arg.Any<SpendAccount>() ) ;
  }
}
