using Fiolyn.Common.Pagination ;
using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Defines operations for managing shared broker accounts. Every account has one or more members
///   via <see cref="BrokerAccountMember" /> — all members are equal and any member can read, edit,
///   delete, manage membership, and CRUD every <see cref="StockTransaction" /> on it.
///   "Not a member" is treated as 404.
/// </summary>
public interface IBrokerAccountService
{
  /// <summary>
  ///   Creates a new broker account and registers the creator as its first member.
  ///   Verifies the creator is a member of the linked <c>SpendAccountId</c>; throws
  ///   <see cref="KeyNotFoundException" /> if not.
  /// </summary>
  /// <param name="account">The broker account to persist (must have <c>SpendAccountId</c> set).</param>
  /// <param name="creatorUserId">The primary key of the creating user; added as initial member.</param>
  /// <returns>The persisted broker account.</returns>
  Task<BrokerAccount> CreateAsync( BrokerAccount account, long creatorUserId ) ;

  /// <summary>
  ///   Gets all active broker accounts the given user is a member of.
  /// </summary>
  /// <param name="userId">The member user's primary key.</param>
  /// <param name="pagination">Page / pageSize for offset-based slicing.</param>
  /// <returns>Paged list of the user's active broker accounts, ordered by name (Id tiebreaker).</returns>
  Task<PagedResult<BrokerAccount>> GetAllForUserAsync( long userId, PaginationQuery pagination ) ;

  /// <summary>
  ///   Gets a single broker account by ID, requiring the caller to be a member.
  /// </summary>
  /// <param name="id">The broker account primary key.</param>
  /// <param name="userId">The caller's primary key.</param>
  /// <returns>The broker account, or null if not found or caller is not a member.</returns>
  Task<BrokerAccount?> GetByIdForUserAsync( long id, long userId ) ;

  /// <summary>
  ///   Checks whether an active broker account exists for the given ID and caller is a member.
  /// </summary>
  /// <param name="id">The broker account primary key.</param>
  /// <param name="userId">The caller's primary key.</param>
  /// <returns>True if an active broker account exists and caller is a member; otherwise false.</returns>
  Task<bool> ExistsForUserAsync( long id, long userId ) ;

  /// <summary>
  ///   Updates a broker account's <c>Name</c> and <c>SpendAccountId</c>, requiring the caller to be a
  ///   member. Returns null if no matching broker is found or the caller is not a member (controller
  ///   maps to 404). When the new <c>SpendAccountId</c> differs from the existing one, the caller's
  ///   membership of the new spend account is verified — throws <see cref="KeyNotFoundException" />
  ///   if the caller is not a member. The returned entity has the (potentially refetched) updated
  ///   <c>SpendAccount</c> nav populated.
  /// </summary>
  /// <param name="id">The broker account primary key.</param>
  /// <param name="userId">The caller's primary key.</param>
  /// <param name="name">The new broker name.</param>
  /// <param name="spendAccountId">The new linked spend-account primary key.</param>
  /// <returns>The updated broker account, or null if not found or caller is not a member.</returns>
  Task<BrokerAccount?> UpdateAsync( long id, long userId, string name, long spendAccountId ) ;

  /// <summary>
  ///   Soft-deletes a broker account by ID, requiring the caller to be a member.
  ///   Throws <see cref="KeyNotFoundException" /> if no matching account is found or caller is not a member.
  /// </summary>
  /// <param name="id">The broker account primary key.</param>
  /// <param name="userId">The caller's primary key.</param>
  Task SoftDeleteAsync( long id, long userId ) ;

  /// <summary>
  ///   Lists the active members of the account. Caller must be a member.
  /// </summary>
  /// <param name="accountId">The broker account primary key.</param>
  /// <param name="callerUserId">The caller's primary key.</param>
  /// <param name="pagination">Page / pageSize for offset-based slicing.</param>
  /// <returns>Paged list of active members, ordered by join time (Id tiebreaker).</returns>
  Task<PagedResult<BrokerAccountMember>> GetMembersAsync(
    long accountId, long callerUserId, PaginationQuery pagination ) ;

  /// <summary>
  ///   Adds a user (identified by email) to the account. Caller must be a member.
  ///   Idempotent: a second call with an already-joined email returns the current member list.
  ///   Throws <see cref="KeyNotFoundException" /> if caller is not a member or the target user is unknown.
  /// </summary>
  /// <param name="accountId">The broker account primary key.</param>
  /// <param name="callerUserId">The caller's primary key.</param>
  /// <param name="email">The email of the user to add (normalized internally).</param>
  /// <returns>The account's active members after the add.</returns>
  Task<List<BrokerAccountMember>> AddMemberAsync( long accountId, long callerUserId, string email ) ;

  /// <summary>
  ///   Removes a user from the account. Caller must be a member.
  ///   Throws <see cref="KeyNotFoundException" /> if caller is not a member or target is not a member.
  ///   Throws <see cref="Fiolyn.Infrastructure.Exceptions.BadRequestException" /> if the removal would
  ///   leave the account with zero members (caller should delete the account instead).
  /// </summary>
  /// <param name="accountId">The broker account primary key.</param>
  /// <param name="callerUserId">The caller's primary key.</param>
  /// <param name="targetUserId">The primary key of the user to remove.</param>
  Task RemoveMemberAsync( long accountId, long callerUserId, long targetUserId ) ;

  /// <summary>
  ///   (Admin) Gets active broker accounts eager-loading all members. Optionally filter to a single
  ///   user's memberships. Ordered by account name with an Id tiebreaker.
  /// </summary>
  /// <param name="pagination">Page / pageSize for offset-based slicing.</param>
  /// <param name="userId">Optional — when supplied, returns only accounts that user is a member of.</param>
  Task<PagedResult<BrokerAccount>> GetAllAsync( PaginationQuery pagination, long? userId = null ) ;

  /// <summary>
  ///   (Admin) Gets any active broker account by ID without user-scoping, eager-loading all members.
  /// </summary>
  /// <param name="id">The broker account primary key.</param>
  /// <returns>The broker account, or null if not found.</returns>
  Task<BrokerAccount?> GetByIdAsync( long id ) ;

  /// <summary>
  ///   (Admin) Soft-deletes a broker account by ID without user-scoping.
  ///   Throws <see cref="KeyNotFoundException" /> if not found.
  /// </summary>
  /// <param name="id">The broker account primary key.</param>
  Task SoftDeleteAsync( long id ) ;

  /// <summary>
  ///   (Admin) Updates any broker account's <c>Name</c> and <c>SpendAccountId</c> without
  ///   user-scoping. Returns null if the broker does not exist (controller maps to 404). Verifies the
  ///   target <c>SpendAccountId</c> references an active spend account; throws
  ///   <see cref="KeyNotFoundException" /> if not.
  /// </summary>
  /// <param name="id">The broker account primary key.</param>
  /// <param name="name">The new broker name.</param>
  /// <param name="spendAccountId">The new linked spend-account primary key.</param>
  /// <returns>The updated broker account, or null if not found.</returns>
  Task<BrokerAccount?> UpdateAsync( long id, string name, long spendAccountId ) ;
}
