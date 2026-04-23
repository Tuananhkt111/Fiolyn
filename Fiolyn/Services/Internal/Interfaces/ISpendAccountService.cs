using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Defines operations for managing user-owned spend accounts.
/// </summary>
public interface ISpendAccountService
{
  /// <summary>
  ///   Creates a new spend account. Caller is responsible for setting <see cref="SpendAccount.AppUserId" />.
  /// </summary>
  /// <param name="account">The spend account to persist.</param>
  /// <returns>The persisted spend account.</returns>
  Task<SpendAccount> CreateAsync( SpendAccount account ) ;

  /// <summary>
  ///   Gets all active spend accounts owned by the given user.
  /// </summary>
  /// <param name="userId">The owning user's primary key.</param>
  /// <returns>List of the user's active spend accounts, ordered by name.</returns>
  Task<List<SpendAccount>> GetAllForUserAsync( long userId ) ;

  /// <summary>
  ///   Gets a single spend account by ID, scoped to the given user.
  /// </summary>
  /// <param name="id">The spend account primary key.</param>
  /// <param name="userId">The owning user's primary key.</param>
  /// <returns>The spend account, or null if not found or not owned by the user.</returns>
  Task<SpendAccount?> GetByIdForUserAsync( long id, long userId ) ;

  /// <summary>
  ///   Updates an existing spend account.
  /// </summary>
  /// <param name="account">The spend account with updated values.</param>
  Task UpdateAsync( SpendAccount account ) ;

  /// <summary>
  ///   Soft-deletes a spend account by ID, scoped to the given user.
  ///   Throws <see cref="KeyNotFoundException" /> if no matching account is found.
  /// </summary>
  /// <param name="id">The spend account primary key.</param>
  /// <param name="userId">The owning user's primary key.</param>
  Task SoftDeleteAsync( long id, long userId ) ;

  /// <summary>
  ///   (Admin) Gets active spend accounts eager-loading the owning <see cref="AppUser" />.
  ///   Optionally filter to one user. Ordered by <c>AppUserId</c> then <c>Name</c>.
  /// </summary>
  /// <param name="userId">Optional — when supplied, scopes results to that user.</param>
  Task<List<SpendAccount>> GetAllAsync( long? userId = null ) ;

  /// <summary>
  ///   (Admin) Gets any active spend account by ID without user-scoping, eager-loading the owning <see cref="AppUser" />.
  /// </summary>
  /// <param name="id">The spend account primary key.</param>
  /// <returns>The spend account, or null if not found.</returns>
  Task<SpendAccount?> GetByIdAsync( long id ) ;

  /// <summary>
  ///   (Admin) Soft-deletes a spend account by ID without user-scoping.
  ///   Throws <see cref="KeyNotFoundException" /> if not found.
  /// </summary>
  /// <param name="id">The spend account primary key.</param>
  Task SoftDeleteAsync( long id ) ;
}
