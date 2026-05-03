using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;
using Fiolyn.Enums ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Entities ;

/// <summary>
///   Admin-managed sub-classifier for transactions, bound to exactly one <see cref="TransactionKind" />
///   so e.g. an "Expense"-kind category cannot be assigned to an income transaction. Unique within a
///   kind: the same name may be reused under different kinds (e.g. "Other" under both Income and
///   Expense).
/// </summary>
[Index( nameof( Kind ), nameof( Name ), IsUnique = true )]
public class TransactionCategory : IAuditable, ISoftDelete
{
  /// <summary>
  ///   Gets or sets the unique identifier (primary key, auto-increment).
  /// </summary>
  [Key]
  public long Id { get ; set ; }

  /// <summary>
  ///   Gets or sets the display name of the category, e.g. "Groceries", "Salary".
  /// </summary>
  [Required]
  [MaxLength( 50 )]
  public string Name { get ; set ; } = string.Empty ;

  /// <summary>
  ///   Gets or sets the transaction kind this category applies to. Immutable post-create.
  /// </summary>
  [Required]
  public TransactionKind Kind { get ; set ; }

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
