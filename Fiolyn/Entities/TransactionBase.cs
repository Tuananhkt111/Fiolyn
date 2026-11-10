using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;

namespace Fiolyn.Entities ;

/// <summary>
///   Abstract base for every money-movement event that adjusts a <see cref="SpendAccount" />.
///   Concrete leaves (Income/Expense/Lend/Borrowing/Transfer/Adjustment/Stock) live in their own
///   tables under EF Core's table-per-type mapping and inherit these common columns. The signed
///   <see cref="Amount" /> is what gets applied to <c>SpendAccount.Balance</c>; negative = money
///   out, positive = money in.
/// </summary>
public abstract class TransactionBase : IAuditable, ISoftDelete
{
  /// <summary>
  ///   Gets or sets the unique identifier (primary key, auto-increment, shared across leaf tables).
  /// </summary>
  [Key]
  public long Id { get ; set ; }

  /// <summary>
  ///   Gets or sets the foreign key to the spend account this transaction adjusts. For
  ///   <see cref="TransferTransaction" />, this is the source account.
  /// </summary>
  [Required]
  public long SpendAccountId { get ; set ; }

  /// <summary>
  ///   Gets or sets the associated spend account.
  /// </summary>
  public SpendAccount SpendAccount { get ; set ; } = null! ;

  /// <summary>
  ///   Gets or sets the optional foreign key to a <see cref="TransactionCategory" />. The category's
  ///   <c>Kind</c> must match this leaf's transaction kind (validated server-side).
  /// </summary>
  public long? CategoryId { get ; set ; }

  /// <summary>
  ///   Gets or sets the associated transaction category.
  /// </summary>
  public TransactionCategory? Category { get ; set ; }

  /// <summary>
  ///   Gets or sets the UTC date of the transaction.
  /// </summary>
  [Required]
  public DateTime Date { get ; set ; }

  /// <summary>
  ///   Gets or sets the optional free-text description. Null becomes empty: the column is NOT NULL,
  ///   and a request that omits the field binds it as null (Newtonsoft ignores a record's default).
  /// </summary>
  [MaxLength( 500 )]
  public string Description
  {
    get ;
    set => field = value ?? string.Empty ;
  } = string.Empty ;

  /// <summary>
  ///   Gets or sets the optional foreign key to the <see cref="AppUser" /> attributed as the payer.
  ///   When omitted on create, the controller fills with the caller's id.
  /// </summary>
  public long? PayerId { get ; set ; }

  /// <summary>
  ///   Gets or sets the user attributed as the payer.
  /// </summary>
  public AppUser? Payer { get ; set ; }

  /// <summary>
  ///   Gets or sets the signed amount applied to <c>SpendAccount.Balance</c> in whole VND. Negative =
  ///   money out, positive = money in.
  /// </summary>
  [Required]
  public long Amount { get ; set ; }

  /// <summary>
  ///   Gets or sets the optional self-referential FK to the parent transaction. Set on
  ///   auto-created <see cref="LendTransaction" /> / <see cref="BorrowingTransaction" /> rows
  ///   that originated from sharing an <see cref="IncomeTransaction" /> or
  ///   <see cref="ExpenseTransaction" /> with a counterparty by percent. Standalone Lend /
  ///   Borrow rows leave this null.
  /// </summary>
  public long? ParentTransactionId { get ; set ; }

  public TransactionBase? ParentTransaction { get ; set ; }

  /// <summary>
  ///   Gets or sets the linked children — populated only for shared Income / Expense rows.
  ///   Each child is a <see cref="LendTransaction" /> (Expense parent) or
  ///   <see cref="BorrowingTransaction" /> (Income parent) carrying a counterparty's slice.
  /// </summary>
  public ICollection<TransactionBase> Children { get ; set ; } = new List<TransactionBase>() ;

  /// <summary>
  ///   Gets or sets the UTC timestamp when this record was created.
  /// </summary>
  [Required]
  public DateTime Created { get ; set ; }

  /// <summary>
  ///   Gets or sets the UTC timestamp when this record was last modified.
  /// </summary>
  [Required]
  public DateTime LastModified { get ; set ; }

  /// <summary>
  ///   Gets or sets a value indicating whether this record has been soft-deleted.
  /// </summary>
  [Required]
  public bool IsDeleted { get ; set ; }
}
