using System.Net ;
using System.Net.Http.Json ;
using System.Text.Json ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Tests.Integration.Helpers ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Tests.Integration.Api ;

/// <summary>FRS v1.0 acceptance criteria for transactions, driven through the HTTP API.</summary>
public class TransactionAcceptanceTests : IDisposable
{
  private const long StartBalance = 1_000_000 ;
  private static readonly DateTime Now = new( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc ) ;

  private readonly FiolynApiFactory _factory = new() ;

  public void Dispose() => _factory.Dispose() ;

  private async Task<AppUser> SeedUserAsync()
  {
    var user = new AppUser
    {
      Email = "owner@example.com",
      GoogleSub = "sub-owner",
      Created = Now,
      LastModified = Now
    } ;
    await _factory.SeedAsync( user ) ;
    return user ;
  }

  private async Task<SpendAccount> SeedSpendAccountAsync(
    AppUser user, string name = "Main", long balance = StartBalance )
  {
    var account = new SpendAccount
    {
      Name = name,
      Type = SpendAccountType.Cash,
      Balance = balance,
      Created = Now,
      LastModified = Now,
      Members = [new SpendAccountMember { AppUserId = user.Id, JoinedAt = Now }]
    } ;
    await _factory.SeedAsync( account ) ;
    return account ;
  }

  private async Task<BrokerAccount> SeedBrokerAsync( AppUser user, SpendAccount spend )
  {
    var broker = new BrokerAccount
    {
      Name = "VPS",
      SpendAccountId = spend.Id,
      Created = Now,
      LastModified = Now,
      Members = [new BrokerAccountMember { AppUserId = user.Id, JoinedAt = Now }]
    } ;
    await _factory.SeedAsync( broker ) ;
    return broker ;
  }

  private async Task<TransactionCategory> SeedCategoryAsync( TransactionKind kind )
  {
    var category = new TransactionCategory
    {
      Name = $"{kind} category",
      Kind = kind,
      Created = Now,
      LastModified = Now
    } ;
    await _factory.SeedAsync( category ) ;
    return category ;
  }

  private Task<long> BalanceAsync( long spendAccountId ) =>
    _factory.QueryAsync( db => db.SpendAccount.AsNoTracking()
      .Where( a => a.Id == spendAccountId ).Select( a => a.Balance ).SingleAsync() ) ;

  private static async Task<long> IdOfAsync( HttpResponseMessage response )
  {
    var body = await response.Content.ReadFromJsonAsync<JsonElement>() ;
    return body.GetProperty( "id" ).GetInt64() ;
  }

  private static object StockBody( string symbol, long brokerId, long? categoryId = null ) => new
  {
    symbol,
    stockTransactionType = "Buy",
    quantity = 100,
    price = 50_000,
    totalAmount = 5_000_000,
    date = Now,
    description = "test",
    brokerAccountId = brokerId,
    categoryId
  } ;

  [Fact]
  [Trait( "Story", "US-17" )]
  public async Task CreateIncome_PositiveAmount_IncreasesBalanceByAmount()
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;

    var response = await _factory.As( user ).PostAsJsonAsync( "api/income-transactions", new
    {
      spendAccountId = account.Id,
      amount = 250_000,
      date = Now,
      description = "test"
    } ) ;

    response.StatusCode.Should().Be( HttpStatusCode.Created ) ;
    ( await BalanceAsync( account.Id ) ).Should().Be( 1_250_000 ) ;
  }

  [Fact]
  [Trait( "Story", "US-17" )]
  public async Task CreateExpense_PositiveAmount_DecreasesBalanceByAmount()
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;

    var response = await _factory.As( user ).PostAsJsonAsync( "api/expense-transactions", new
    {
      spendAccountId = account.Id,
      amount = 250_000,
      date = Now,
      description = "test"
    } ) ;

    response.StatusCode.Should().Be( HttpStatusCode.Created ) ;
    ( await BalanceAsync( account.Id ) ).Should().Be( 750_000 ) ;
  }

  [Fact]
  [Trait( "Story", "US-17" )]
  public async Task DeleteIncome_WithoutShares_ReversesBalanceEffect()
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var client = _factory.As( user ) ;
    var created = await client.PostAsJsonAsync( "api/income-transactions", new
    {
      spendAccountId = account.Id,
      amount = 250_000,
      date = Now,
      description = "test"
    } ) ;
    ( await BalanceAsync( account.Id ) ).Should().Be( 1_250_000 ) ;

    var deleted = await client.DeleteAsync( $"api/income-transactions/{await IdOfAsync( created )}" ) ;

    deleted.StatusCode.Should().Be( HttpStatusCode.NoContent ) ;
    ( await BalanceAsync( account.Id ) ).Should().Be( StartBalance ) ;
  }

  [Fact]
  [Trait( "Story", "US-17" )]
  public async Task DeleteExpense_WithoutShares_ReversesBalanceEffect()
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var client = _factory.As( user ) ;
    var created = await client.PostAsJsonAsync( "api/expense-transactions", new
    {
      spendAccountId = account.Id,
      amount = 250_000,
      date = Now,
      description = "test"
    } ) ;
    ( await BalanceAsync( account.Id ) ).Should().Be( 750_000 ) ;

    var deleted = await client.DeleteAsync( $"api/expense-transactions/{await IdOfAsync( created )}" ) ;

    deleted.StatusCode.Should().Be( HttpStatusCode.NoContent ) ;
    ( await BalanceAsync( account.Id ) ).Should().Be( StartBalance ) ;
  }

  [Fact]
  [Trait( "Story", "US-17" )]
  public async Task DeleteLend_ReversesBalanceEffect()
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var client = _factory.As( user ) ;
    var created = await client.PostAsJsonAsync( "api/lend-transactions", new
    {
      spendAccountId = account.Id,
      amount = 300_000,
      date = Now,
      description = "test",
      counterpartyName = "Binh"
    } ) ;
    created.StatusCode.Should().Be( HttpStatusCode.Created ) ;
    ( await BalanceAsync( account.Id ) ).Should().Be( 700_000 ) ;

    var deleted = await client.DeleteAsync( $"api/lend-transactions/{await IdOfAsync( created )}" ) ;

    deleted.StatusCode.Should().Be( HttpStatusCode.NoContent ) ;
    ( await BalanceAsync( account.Id ) ).Should().Be( StartBalance ) ;
  }

  [Fact]
  [Trait( "Story", "US-18" )]
  public async Task CreateStock_LowercaseSymbol_StoresAndReturnsUppercase()
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var broker = await SeedBrokerAsync( user, account ) ;
    var client = _factory.As( user ) ;

    var created = await client.PostAsJsonAsync( "api/stock-transactions", StockBody( "vnm", broker.Id ) ) ;

    created.StatusCode.Should().Be( HttpStatusCode.Created ) ;
    var id = await IdOfAsync( created ) ;
    var read = await client.GetFromJsonAsync<JsonElement>( $"api/stock-transactions/{id}" ) ;
    read.GetProperty( "symbol" ).GetString().Should().Be( "VNM" ) ;
    var stored = await _factory.QueryAsync( db => db.StockTransaction.AsNoTracking()
      .Where( t => t.Id == id ).Select( t => t.Symbol ).SingleAsync() ) ;
    stored.Should().Be( "VNM" ) ;
  }

  [Theory]
  [Trait( "Story", "US-18" )]
  [InlineData( "vnm" )]
  [InlineData( "VNM" )]
  [InlineData( "Vnm" )]
  public async Task ListStock_SymbolFilterInAnyCase_MatchesOnlyThatSymbol( string filter )
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var broker = await SeedBrokerAsync( user, account ) ;
    var client = _factory.As( user ) ;
    ( await client.PostAsJsonAsync( "api/stock-transactions", StockBody( "vnm", broker.Id ) ) )
      .StatusCode.Should().Be( HttpStatusCode.Created ) ;
    ( await client.PostAsJsonAsync( "api/stock-transactions", StockBody( "fpt", broker.Id ) ) )
      .StatusCode.Should().Be( HttpStatusCode.Created ) ;

    var page = await client.GetFromJsonAsync<JsonElement>( $"api/stock-transactions?symbol={filter}" ) ;

    page.GetProperty( "totalCount" ).GetInt64().Should().Be( 1 ) ;
    var items = page.GetProperty( "items" ).EnumerateArray().ToList() ;
    items.Should().ContainSingle() ;
    items[0].GetProperty( "symbol" ).GetString().Should().Be( "VNM" ) ;
  }

  [Theory]
  [Trait( "Story", "US-18" )]
  [InlineData( TransactionKind.Income )]
  [InlineData( TransactionKind.Expense )]
  [InlineData( TransactionKind.Lend )]
  [InlineData( TransactionKind.Borrowing )]
  [InlineData( TransactionKind.Transfer )]
  [InlineData( TransactionKind.Adjustment )]
  public async Task CreateStock_CategoryOfOtherKind_Returns400AndBalanceUnchanged( TransactionKind kind )
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var broker = await SeedBrokerAsync( user, account ) ;
    var category = await SeedCategoryAsync( kind ) ;

    var response = await _factory.As( user ).PostAsJsonAsync(
      "api/stock-transactions", StockBody( "vnm", broker.Id, category.Id ) ) ;

    response.StatusCode.Should().Be( HttpStatusCode.BadRequest ) ;
    ( await BalanceAsync( account.Id ) ).Should().Be( StartBalance ) ;
    ( await _factory.QueryAsync( db => db.StockTransaction.CountAsync() ) ).Should().Be( 0 ) ;
  }

  [Fact]
  [Trait( "Story", "US-18" )]
  public async Task CreateStock_CategoryOfStockKind_SucceedsAndDebitsBuyTotal()
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var broker = await SeedBrokerAsync( user, account ) ;
    var category = await SeedCategoryAsync( TransactionKind.Stock ) ;

    var response = await _factory.As( user ).PostAsJsonAsync(
      "api/stock-transactions", StockBody( "vnm", broker.Id, category.Id ) ) ;

    response.StatusCode.Should().Be( HttpStatusCode.Created ) ;
    ( await BalanceAsync( account.Id ) ).Should().Be( -4_000_000 ) ;
  }

  [Theory]
  [Trait( "Story", "US-19" )]
  [InlineData( 0 )]
  [InlineData( -1 )]
  [InlineData( -500_000 )]
  public async Task CreateTransfer_AmountBelowOne_Returns400AndBalancesUnchanged( long amount )
  {
    var user = await SeedUserAsync() ;
    var source = await SeedSpendAccountAsync( user, "Source" ) ;
    var destination = await SeedSpendAccountAsync( user, "Destination", 200_000 ) ;

    var response = await _factory.As( user ).PostAsJsonAsync( "api/transfer-transactions", new
    {
      spendAccountId = source.Id,
      destinationSpendAccountId = destination.Id,
      amount,
      date = Now,
      description = "test"
    } ) ;

    response.StatusCode.Should().Be( HttpStatusCode.BadRequest ) ;
    ( await BalanceAsync( source.Id ) ).Should().Be( StartBalance ) ;
    ( await BalanceAsync( destination.Id ) ).Should().Be( 200_000 ) ;
    ( await _factory.QueryAsync( db => db.TransferTransaction.CountAsync() ) ).Should().Be( 0 ) ;
  }

  private static Dictionary<string, object?> WithDescription(
    Dictionary<string, object?> body, string mode )
  {
    switch ( mode )
    {
      case "null":
        body["description"] = null ;
        break ;
      case "omitted":
        break ;
      default:
        throw new ArgumentOutOfRangeException( nameof( mode ) ) ;
    }

    return body ;
  }

  private async Task AssertCreatedWithEmptyDescriptionAsync(
    AppUser user, string route, Dictionary<string, object?> body )
  {
    var client = _factory.As( user ) ;

    var response = await client.PostAsJsonAsync( route, body ) ;

    response.StatusCode.Should().Be( HttpStatusCode.Created ) ;
    var id = await IdOfAsync( response ) ;
    var read = await client.GetFromJsonAsync<JsonElement>( $"{route}/{id}" ) ;
    read.GetProperty( "description" ).GetString().Should().Be( string.Empty ) ;
  }

  [Theory]
  [Trait( "Story", "US-17" )]
  [InlineData( "income-transactions", "omitted" )]
  [InlineData( "income-transactions", "null" )]
  [InlineData( "expense-transactions", "omitted" )]
  [InlineData( "expense-transactions", "null" )]
  [InlineData( "lend-transactions", "omitted" )]
  [InlineData( "lend-transactions", "null" )]
  [InlineData( "borrowing-transactions", "omitted" )]
  [InlineData( "borrowing-transactions", "null" )]
  [InlineData( "adjustment-transactions", "omitted" )]
  [InlineData( "adjustment-transactions", "null" )]
  public async Task Create_DescriptionOmittedOrNull_Returns201WithEmptyDescription( string kind, string mode )
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var body = new Dictionary<string, object?>
    {
      ["spendAccountId"] = account.Id,
      ["amount"] = 250_000,
      ["date"] = Now
    } ;
    if ( kind is "lend-transactions" or "borrowing-transactions" )
    {
      body["counterpartyName"] = "Binh" ;
    }

    await AssertCreatedWithEmptyDescriptionAsync( user, $"api/{kind}", WithDescription( body, mode ) ) ;
  }

  [Theory]
  [Trait( "Story", "US-18" )]
  [InlineData( "omitted" )]
  [InlineData( "null" )]
  public async Task CreateStock_DescriptionOmittedOrNull_Returns201WithEmptyDescription( string mode )
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var broker = await SeedBrokerAsync( user, account ) ;
    var body = new Dictionary<string, object?>
    {
      ["symbol"] = "vnm",
      ["stockTransactionType"] = "Buy",
      ["quantity"] = 100,
      ["price"] = 50_000,
      ["totalAmount"] = 5_000_000,
      ["date"] = Now,
      ["brokerAccountId"] = broker.Id
    } ;

    await AssertCreatedWithEmptyDescriptionAsync( user, "api/stock-transactions", WithDescription( body, mode ) ) ;
  }

  [Theory]
  [Trait( "Story", "US-19" )]
  [InlineData( "omitted" )]
  [InlineData( "null" )]
  public async Task CreateTransfer_DescriptionOmittedOrNull_Returns201WithEmptyDescription( string mode )
  {
    var user = await SeedUserAsync() ;
    var source = await SeedSpendAccountAsync( user, "Source" ) ;
    var destination = await SeedSpendAccountAsync( user, "Destination", 200_000 ) ;
    var body = new Dictionary<string, object?>
    {
      ["spendAccountId"] = source.Id,
      ["destinationSpendAccountId"] = destination.Id,
      ["amount"] = 250_000,
      ["date"] = Now
    } ;

    await AssertCreatedWithEmptyDescriptionAsync( user, "api/transfer-transactions", WithDescription( body, mode ) ) ;
  }

  private async Task AssertUpdateWithEmptyDescriptionAsync(
    AppUser user, string route, Dictionary<string, object?> body, string mode )
  {
    var client = _factory.As( user ) ;
    var withText = new Dictionary<string, object?>( body ) { ["description"] = "lunch" } ;
    var created = await client.PostAsJsonAsync( route, withText ) ;
    created.StatusCode.Should().Be( HttpStatusCode.Created ) ;
    var id = await IdOfAsync( created ) ;

    var response = await client.PutAsJsonAsync( $"{route}/{id}", WithDescription( body, mode ) ) ;

    response.StatusCode.Should().Be( HttpStatusCode.OK ) ;
    var read = await client.GetFromJsonAsync<JsonElement>( $"{route}/{id}" ) ;
    read.GetProperty( "description" ).GetString().Should().Be( string.Empty ) ;
  }

  [Theory]
  [Trait( "Story", "US-17" )]
  [InlineData( "income-transactions", "omitted" )]
  [InlineData( "income-transactions", "null" )]
  [InlineData( "expense-transactions", "omitted" )]
  [InlineData( "expense-transactions", "null" )]
  [InlineData( "lend-transactions", "omitted" )]
  [InlineData( "lend-transactions", "null" )]
  [InlineData( "borrowing-transactions", "omitted" )]
  [InlineData( "borrowing-transactions", "null" )]
  [InlineData( "adjustment-transactions", "omitted" )]
  [InlineData( "adjustment-transactions", "null" )]
  public async Task Update_DescriptionOmittedOrNull_Returns200WithEmptyDescription( string kind, string mode )
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var body = new Dictionary<string, object?>
    {
      ["spendAccountId"] = account.Id,
      ["amount"] = 250_000,
      ["date"] = Now
    } ;
    if ( kind is "lend-transactions" or "borrowing-transactions" )
    {
      body["counterpartyName"] = "Binh" ;
    }

    await AssertUpdateWithEmptyDescriptionAsync( user, $"api/{kind}", body, mode ) ;
  }

  [Theory]
  [Trait( "Story", "US-18" )]
  [InlineData( "omitted" )]
  [InlineData( "null" )]
  public async Task UpdateStock_DescriptionOmittedOrNull_Returns200WithEmptyDescription( string mode )
  {
    var user = await SeedUserAsync() ;
    var account = await SeedSpendAccountAsync( user ) ;
    var broker = await SeedBrokerAsync( user, account ) ;
    var body = new Dictionary<string, object?>
    {
      ["symbol"] = "vnm",
      ["stockTransactionType"] = "Buy",
      ["quantity"] = 100,
      ["price"] = 50_000,
      ["totalAmount"] = 5_000_000,
      ["date"] = Now,
      ["brokerAccountId"] = broker.Id
    } ;

    await AssertUpdateWithEmptyDescriptionAsync( user, "api/stock-transactions", body, mode ) ;
  }

  [Theory]
  [Trait( "Story", "US-19" )]
  [InlineData( "omitted" )]
  [InlineData( "null" )]
  public async Task UpdateTransfer_DescriptionOmittedOrNull_Returns200WithEmptyDescription( string mode )
  {
    var user = await SeedUserAsync() ;
    var source = await SeedSpendAccountAsync( user, "Source" ) ;
    var destination = await SeedSpendAccountAsync( user, "Destination", 200_000 ) ;
    var body = new Dictionary<string, object?>
    {
      ["spendAccountId"] = source.Id,
      ["destinationSpendAccountId"] = destination.Id,
      ["amount"] = 250_000,
      ["date"] = Now
    } ;

    await AssertUpdateWithEmptyDescriptionAsync( user, "api/transfer-transactions", body, mode ) ;
  }

  [Fact]
  [Trait( "Story", "US-19" )]
  public async Task CreateTransfer_AmountOfOne_Returns201AndMovesOne()
  {
    var user = await SeedUserAsync() ;
    var source = await SeedSpendAccountAsync( user, "Source" ) ;
    var destination = await SeedSpendAccountAsync( user, "Destination", 200_000 ) ;

    var response = await _factory.As( user ).PostAsJsonAsync( "api/transfer-transactions", new
    {
      spendAccountId = source.Id,
      destinationSpendAccountId = destination.Id,
      amount = 1,
      date = Now,
      description = "test"
    } ) ;

    response.StatusCode.Should().Be( HttpStatusCode.Created ) ;
    ( await BalanceAsync( source.Id ) ).Should().Be( 999_999 ) ;
    ( await BalanceAsync( destination.Id ) ).Should().Be( 200_001 ) ;
  }
}
