using Fiolyn.Entities ;
using Fiolyn.Enums ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Defines operations for managing shared spend accounts. Every account has one or more members
///   via <see cref="SpendAccountMember" /> — all members are equal and any member can read, edit,
///   delete, or manage membership. "Not a member" is treated as 404.
/// </summary>
public interface ISpendAccountService
{
  /// <summary>
  ///   Creates a new spend account and registers the creator as its first member.
  /// </summary>
  /// <param name="account">The spend account to persist.</param>
  /// <param name="creatorUserId">The primary key of the creating user; added as initial member.</param>
  /// <returns>The persisted spend account.</returns>
  Task<SpendAccount> CreateAsync( SpendAccount account, long creatorUserId ) ;

  /// <summary>
  ///   Gets all active spend accounts the given user is a member of.
  /// </summary>
  /// <param name="userId">The member user's primary key.</param>
  /// <returns>List of the user's active spend accounts, ordered by name.</returns>
  Task<List<SpendAccount>> GetAllForUserAsync( long userId ) ;

  /// <summary>
  ///   Gets a single spend account by ID, requiring the caller to be a member.
  /// </summary>
  /// <param name="id">The spend account primary key.</param>
  /// <param name="userId">The caller's primary key.</param>
  /// <returns>The spend account, or null if not found or caller is not a member.</returns>
  Task<SpendAccount?> GetByIdForUserAsync( long id, long userId ) ;

  /// <summary>
  ///   Updates a spend account's <c>Name</c> and <c>Type</c>, requiring the caller to be a member.
  ///   Returns null if no matching account is found or the caller is not a member (controller maps to
  ///   404). Does NOT persist <c>Balance</c> changes — Balance flows exclusively through transaction
  ///   creates/updates/deletes (e.g. <c>AdjustmentTransaction</c>) so concurrent writes are
  ///   serialized by the <c>[ConcurrencyCheck]</c> on <c>Balance</c>.
  /// </summary>
  /// <param name="id">The spend account primary key.</param>
  /// <param name="userId">The caller's primary key.</param>
  /// <param name="name">The new account name.</param>
  /// <param name="type">The new account type.</param>
  /// <returns>The updated spend account, or null if not found or caller is not a member.</returns>
  Task<SpendAccount?> UpdateAsync( long id, long userId, string name, SpendAccountType type ) ;

  /// <summary>
  ///   Soft-deletes a spend account by ID, requiring the caller to be a member.
  ///   Throws <see cref="KeyNotFoundException" /> if no matching account is found or caller is not a member.
  /// </summary>
  /// <param name="id">The spend account primary key.</param>
  /// <param name="userId">The caller's primary key.</param>
  Task SoftDeleteAsync( long id, long userId ) ;

  /// <summary>
  ///   Lists the active members of the account. Caller must be a member.
  /// </summary>
  /// <param name="accountId">The spend account primary key.</param>
  /// <param name="callerUserId">The caller's primary key.</param>
  /// <returns>The account's active members, ordered by join time.</returns>
  Task<List<SpendAccountMember>> GetMembersAsync( long accountId, long callerUserId ) ;

  /// <summary>
  ///   Adds a user (identified by email) to the account. Caller must be a member.
  ///   Idempotent: a second call with an already-joined email returns the current member list.
  ///   Throws <see cref="KeyNotFoundException" /> if caller is not a member or the target user is unknown.
  /// </summary>
  /// <param name="accountId">The spend account primary key.</param>
  /// <param name="callerUserId">The caller's primary key.</param>
  /// <param name="email">The email of the user to add (normalized internally).</param>
  /// <returns>The account's active members after the add.</returns>
  Task<List<SpendAccountMember>> AddMemberAsync( long accountId, long callerUserId, string email ) ;

  /// <summary>
  ///   Removes a user from the account. Caller must be a member.
  ///   Throws <see cref="KeyNotFoundException" /> if caller is not a member or target is not a member.
  ///   Throws <see cref="Fiolyn.Infrastructure.Exceptions.BadRequestException" /> if the removal would
  ///   leave the account with zero members (caller should delete the account instead).
  /// </summary>
  /// <param name="accountId">The spend account primary key.</param>
  /// <param name="callerUserId">The caller's primary key.</param>
  /// <param name="targetUserId">The primary key of the user to remove.</param>
  Task RemoveMemberAsync( long accountId, long callerUserId, long targetUserId ) ;

  /// <summary>
  ///   (Admin) Gets active spend accounts eager-loading all members. Optionally filter to a single
  ///   user's memberships. Ordered by account name.
  /// </summary>
  /// <param name="userId">Optional — when supplied, returns only accounts that user is a member of.</param>
  Task<List<SpendAccount>> GetAllAsync( long? userId = null ) ;

  /// <summary>
  ///   (Admin) Gets any active spend account by ID without user-scoping, eager-loading all members.
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

  /// <summary>
  ///   (Admin) Updates any spend account's <c>Name</c> and <c>Type</c> without user-scoping. Returns
  ///   null if no matching account is found (controller maps to 404). Balance is intentionally not
  ///   updatable here — every balance change flows through a transaction leaf.
  /// </summary>
  /// <param name="id">The spend account primary key.</param>
  /// <param name="name">The new account name.</param>
  /// <param name="type">The new account type.</param>
  /// <returns>The updated spend account, or null if not found.</returns>
  Task<SpendAccount?> UpdateAsync( long id, string name, SpendAccountType type ) ;
}
