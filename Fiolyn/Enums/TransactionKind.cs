namespace Fiolyn.Enums ;

/// <summary>
///   Discriminator for the concrete child class of a <c>TransactionBase</c>. Bound to a
///   <c>TransactionCategory</c> to scope which leaf type the category applies to.
/// </summary>
public enum TransactionKind
{
  Income,
  Expense,
  Lend,
  Borrowing,
  Transfer,
  Adjustment,
  Stock
}
