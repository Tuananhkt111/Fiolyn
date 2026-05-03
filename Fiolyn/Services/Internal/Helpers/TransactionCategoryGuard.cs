using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal.Helpers ;

/// <summary>
///   Validates that a caller-supplied <c>CategoryId</c> resolves to a row whose
///   <see cref="TransactionCategory.Kind" /> matches the leaf service's kind. A null
///   category id is a no-op (categories are optional).
/// </summary>
internal static class TransactionCategoryGuard
{
  internal static async Task EnsureKindMatchesAsync(
    IRepository<TransactionCategory> categoryRepository,
    long? categoryId,
    TransactionKind expectedKind )
  {
    if ( categoryId is null )
    {
      return ;
    }

    var kind = await categoryRepository.Query()
      .Where( c => c.Id == categoryId.Value )
      .Select( c => ( TransactionKind? )c.Kind )
      .FirstOrDefaultAsync() ;

    if ( kind is null )
    {
      throw new BadRequestException( $"TransactionCategory {categoryId} not found." ) ;
    }

    if ( kind != expectedKind )
    {
      throw new BadRequestException(
        $"Category {categoryId} has Kind={kind}, which does not match {expectedKind}." ) ;
    }
  }
}
