using Fiolyn.Common.Pagination ;
using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Manages <see cref="TransferTransaction" /> records — money moving between two
///   <see cref="SpendAccount" />s the caller is a member of <b>both</b>. The base
///   <c>SpendAccountId</c> is the source (debited <c>-Amount</c>); <c>DestinationSpendAccountId</c>
///   is credited <c>+Amount</c>. Both legs land in one atomic save.
/// </summary>
public interface ITransferTransactionService
{
  Task<TransferTransaction> CreateAsync( TransferTransaction transaction, long userId ) ;
  Task UpdateAsync( TransferTransaction transaction, long userId ) ;
  Task SoftDeleteAsync( long id, long userId ) ;
  Task SoftDeleteAsync( long id ) ;
  Task<PagedResult<TransferTransaction>> GetAllForUserAsync( long userId, PaginationQuery pagination ) ;
  Task<TransferTransaction?> GetByIdForUserAsync( long id, long userId ) ;
  Task<bool> ExistsForUserAsync( long id, long userId ) ;
  Task<PagedResult<TransferTransaction>> GetAllAsync( PaginationQuery pagination, long? userId = null ) ;
  Task<TransferTransaction?> GetByIdAsync( long id ) ;
}
