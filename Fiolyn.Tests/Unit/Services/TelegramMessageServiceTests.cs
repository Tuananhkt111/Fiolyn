using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.External.Notion ;
using Fiolyn.Services.External.Telegram ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.Extensions.Logging ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class TelegramMessageServiceTests
{
  private readonly ILogger<TelegramMessageService> _logger = Substitute.For<ILogger<TelegramMessageService>>() ;
  private readonly INotionService _notionService = Substitute.For<INotionService>() ;
  private readonly IStockTransactionService _stockTransactionService = Substitute.For<IStockTransactionService>() ;
  private readonly IRepository<TelegramUserInfo> _userRepository = Substitute.For<IRepository<TelegramUserInfo>>() ;

  private TelegramMessageService CreateSut() =>
    new(_userRepository, _notionService, _stockTransactionService, _logger) ;

  private void SetupAllowedUser( long telegramId )
  {
    List<TelegramUserInfo> users =
    [
      new()
      {
        TelegramId = telegramId,
        IsDeleted = false
      }
    ] ;
    _userRepository.QueryActive().Returns( users.BuildMock() ) ;
  }

  private void SetupNoAllowedUsers()
  {
    List<TelegramUserInfo> users = [] ;
    _userRepository.QueryActive().Returns( users.BuildMock() ) ;
  }

  [Fact]
  public async Task ProcessWebhookAsync_MissingMessageField_ReturnsSkipped()
  {
    var sut = CreateSut() ;

    var result = await sut.ProcessWebhookAsync( """{"update_id":1}""" ) ;

    result.IsSuccess.Should().BeFalse() ;
    result.Message.Should().Contain( "Skipped" ) ;
  }

  [Fact]
  public async Task ProcessWebhookAsync_MissingFromField_ReturnsSkipped()
  {
    var sut = CreateSut() ;

    var result = await sut.ProcessWebhookAsync( """{"message":{"text":"hello"}}""" ) ;

    result.IsSuccess.Should().BeFalse() ;
    result.Message.Should().Contain( "Skipped" ) ;
  }

  [Fact]
  public async Task ProcessWebhookAsync_UserNotAllowed_ReturnsSkippedAndDoesNotCallNotion()
  {
    SetupNoAllowedUsers() ;
    var sut = CreateSut() ;

    var result = await sut.ProcessWebhookAsync( """{"message":{"from":{"id":999},"text":"hello"}}""" ) ;

    result.IsSuccess.Should().BeFalse() ;
    result.Message.Should().Contain( "Skipped" ) ;
    await _notionService.DidNotReceive().AppendAsync( Arg.Any<string>() ) ;
  }

  [Fact]
  public async Task ProcessWebhookAsync_AllowedUserNoTextField_ReturnsSkipped()
  {
    SetupAllowedUser( 123L ) ;
    var sut = CreateSut() ;

    var result = await sut.ProcessWebhookAsync( """{"message":{"from":{"id":123}}}""" ) ;

    result.IsSuccess.Should().BeFalse() ;
    result.Message.Should().Contain( "Skipped" ) ;
  }

  [Fact]
  public async Task ProcessWebhookAsync_AllowedUserEmptyText_ReturnsSkipped()
  {
    SetupAllowedUser( 123L ) ;
    var sut = CreateSut() ;

    var result = await sut.ProcessWebhookAsync( """{"message":{"from":{"id":123},"text":""}}""" ) ;

    result.IsSuccess.Should().BeFalse() ;
    result.Message.Should().Contain( "Skipped" ) ;
  }

  [Fact]
  public async Task ProcessWebhookAsync_ValidMessage_ReturnsSuccessAndCallsNotion()
  {
    SetupAllowedUser( 123L ) ;
    var sut = CreateSut() ;

    var result = await sut.ProcessWebhookAsync( """{"message":{"from":{"id":123},"text":"buy milk"}}""" ) ;

    result.IsSuccess.Should().BeTrue() ;
    result.Message.Should().Be( "Success" ) ;
    await _notionService.Received( 1 ).AppendAsync( "buy milk" ) ;
  }

  [Fact]
  public async Task ProcessWebhookAsync_InvalidJson_ReturnsError()
  {
    var sut = CreateSut() ;

    var result = await sut.ProcessWebhookAsync( "not valid json {{" ) ;

    result.IsSuccess.Should().BeFalse() ;
    result.Message.Should().Contain( "Error" ) ;
  }

  [Fact]
  public async Task ProcessWebhookAsync_NotionThrows_ReturnsError()
  {
    SetupAllowedUser( 123L ) ;
    _notionService.AppendAsync( Arg.Any<string>() )
      .Returns( Task.FromException( new InvalidOperationException( "Notion API failed" ) ) ) ;
    var sut = CreateSut() ;

    var result = await sut.ProcessWebhookAsync( """{"message":{"from":{"id":123},"text":"buy milk"}}""" ) ;

    result.IsSuccess.Should().BeFalse() ;
    result.Message.Should().Contain( "Error" ) ;
  }

  [Fact]
  public async Task ProcessWebhookAsync_StockMessage_SavesTransactionSkipsNotion()
  {
    SetupAllowedUser( 123L ) ;
    var parsedTransaction = new StockTransaction
    {
      Symbol = "VNM",
      TransactionType = TransactionType.Buy,
      Quantity = 100,
      Price = 45000,
      TotalAmount = 4501500
    } ;
    _stockTransactionService.ParseMessageAsync( Arg.Any<string>() )
      .Returns( Task.FromResult<StockTransaction?>( parsedTransaction ) ) ;
    _stockTransactionService.CreateFromTelegramAsync( parsedTransaction ).Returns( parsedTransaction ) ;
    var sut = CreateSut() ;

    var result = await sut.ProcessWebhookAsync(
      """{"message":{"from":{"id":123},"text":"stock buy VNM 100 45000 4501500"}}""" ) ;

    result.IsSuccess.Should().BeTrue() ;
    await _stockTransactionService.Received( 1 ).ParseMessageAsync( "stock buy VNM 100 45000 4501500" ) ;
    await _stockTransactionService.Received( 1 ).CreateFromTelegramAsync( parsedTransaction ) ;
    await _notionService.DidNotReceive().AppendAsync( Arg.Any<string>() ) ;
  }

  [Fact]
  public async Task ProcessWebhookAsync_InvalidStockFormat_ReturnsError()
  {
    SetupAllowedUser( 123L ) ;
    _stockTransactionService.ParseMessageAsync( Arg.Any<string>() )
      .Returns( Task.FromResult<StockTransaction?>( null ) ) ;
    var sut = CreateSut() ;

    var result = await sut.ProcessWebhookAsync(
      """{"message":{"from":{"id":123},"text":"stock invalid"}}""" ) ;

    result.IsSuccess.Should().BeFalse() ;
    result.Message.Should().Contain( "Error" ) ;
    await _notionService.DidNotReceive().AppendAsync( Arg.Any<string>() ) ;
    await _stockTransactionService.DidNotReceive().CreateFromTelegramAsync( Arg.Any<StockTransaction>() ) ;
  }
}
