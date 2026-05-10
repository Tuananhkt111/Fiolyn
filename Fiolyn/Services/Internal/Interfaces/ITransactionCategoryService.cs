using Fiolyn.Common.Pagination ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Admin-managed sub-classifier for transactions. Each <see cref="TransactionCategory" /> is bound
///   to exactly one <see cref="TransactionKind" />; the leaf transaction services validate the kind
///   match on every create/update.
/// </summary>
public interface ITransactionCategoryService
{
  /// <summary>
  ///   Creates a new category. Throws <see cref="Fiolyn.Infrastructure.Exceptions.ConflictException" />
  ///   on duplicate <c>(Kind, Name)</c>.
  /// </summary>
  Task<TransactionCategory> CreateAsync( string name, TransactionKind kind ) ;

  /// <summary>
  ///   Renames an existing category. <c>Kind</c> is immutable post-create; updating it is not
  ///   supported (would invalidate every transaction that references the row).
  ///   Throws <see cref="KeyNotFoundException" /> if not found.
  ///   Throws <see cref="Fiolyn.Infrastructure.Exceptions.ConflictException" /> on duplicate
  ///   <c>(Kind, Name)</c>.
  /// </summary>
  Task<TransactionCategory> UpdateAsync( long id, string name ) ;

  /// <summary>
  ///   Soft-deletes a category. Existing transactions keep their reference until next overwrite —
  ///   the FK is configured <c>OnDelete = SetNull</c> so a hard delete (rare) zeroes the link
  ///   instead of orphaning the row.
  /// </summary>
  Task SoftDeleteAsync( long id ) ;

  /// <summary>
  ///   Lists active categories, optionally filtered by kind. Ordered by name with an Id tiebreaker.
  /// </summary>
  Task<PagedResult<TransactionCategory>> GetAllAsync(
    PaginationQuery pagination, TransactionKind? kind = null ) ;

  /// <summary>
  ///   Gets a category by id. Returns null when not found.
  /// </summary>
  Task<TransactionCategory?> GetByIdAsync( long id ) ;
}
