using System.ComponentModel.DataAnnotations ;
using System.Reflection ;
using Fiolyn.Controllers.DTOs.Requests.Shared ;
using FluentAssertions ;

namespace Fiolyn.Tests.Unit.Controllers ;

/// <summary>
///   v1.0 US-24: a share percentage is rounded to 2 decimals (half away from zero) and only then
///   range-checked against [0.01, 100]. The request itself must not reject a value that rounds into
///   that range; the service range-check after rounding is covered in IncomeTransactionServiceTests.
///   The attributes sit on the positional record's constructor parameters, which is what ASP.NET Core
///   validates, so they are read from there (Validator.TryValidateObject only sees properties).
/// </summary>
public class TransactionShareRequestTests
{
  private static bool ParameterAccepts( string parameter, object? value ) =>
    typeof( TransactionShareRequest ).GetConstructors().Single().GetParameters()
      .Single( p => p.Name == parameter )
      .GetCustomAttributes<ValidationAttribute>()
      .All( a => a.IsValid( value ) ) ;

  [Theory]
  [Trait( "Story", "US-24" )]
  [InlineData( 0.005 )]
  [InlineData( 100.004 )]
  public void Percentage_ThatRoundsIntoRange_PassesRequestValidation( double percentage ) =>
    ParameterAccepts( nameof( TransactionShareRequest.Percentage ), percentage ).Should().BeTrue() ;

  [Fact]
  [Trait( "Story", "US-24" )]
  public void CounterpartyName_IsStillRequired() =>
    ParameterAccepts( nameof( TransactionShareRequest.CounterpartyName ), null ).Should().BeFalse() ;
}
