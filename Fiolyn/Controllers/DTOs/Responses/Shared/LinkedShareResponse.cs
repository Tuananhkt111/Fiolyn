using Fiolyn.Entities ;
using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.Shared ;

/// <summary>
///   A counterparty's slice of a shared Income / Expense transaction, materialised as a linked
///   <see cref="Entities.LendTransaction" /> (Expense parent → counterparty owes you) or
///   <see cref="Entities.BorrowingTransaction" /> (Income parent → you owe counterparty).
///   <see cref="Amount" /> is the child's signed amount (exact, the source of truth).
///   <see cref="Percentage" /> is recovered from the stored amounts, rounded to 2 decimals,
///   and is approximate when the original total didn't divide evenly — for display only.
/// </summary>
public record LinkedShareResponse(
  long Id,
  TransactionKind Kind,
  long CounterpartyId,
  string CounterpartyName,
  long Amount,
  double Percentage )
{
  public static IReadOnlyList<LinkedShareResponse> FromParent( TransactionBase parent )
  {
    if ( parent.Children.Count == 0 )
    {
      return [] ;
    }

    var totalMagnitude = Math.Abs( parent.Amount + parent.Children.Sum( c => c.Amount ) ) ;
    return parent.Children.Select( child =>
    {
      var counterparty = ( child as IHasCounterparty )?.Counterparty ;
      var kind = child switch
      {
        Entities.LendTransaction => TransactionKind.Lend,
        Entities.BorrowingTransaction => TransactionKind.Borrowing,
        _ => throw new InvalidOperationException(
          $"Unsupported share child type {child.GetType().Name}." )
      } ;
      var percentage = Math.Round(
        Math.Abs( child.Amount ) * 100.0 / totalMagnitude, 2, MidpointRounding.AwayFromZero ) ;
      return new LinkedShareResponse(
        child.Id,
        kind,
        counterparty?.Id ?? 0L,
        counterparty?.Name ?? string.Empty,
        child.Amount,
        percentage ) ;
    } ).ToList() ;
  }
}
