using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal.Helpers ;

/// <summary>
///   Resolves a free-text counterparty name supplied on a lend/borrowing request to a
///   <see cref="Counterparty" /> row owned by the caller. Returns the existing row when a
///   case-insensitive match is found; otherwise stages a new row via <c>AddAsync</c> (no save) so
///   the leaf service's subsequent <c>SaveAsync</c> commits the new counterparty and the
///   transaction in the same transaction.
/// </summary>
internal static class CounterpartyResolver
{
  internal static async Task<Counterparty> ResolveOrCreateAsync(
    IRepository<Counterparty> repository, string rawName, long userId )
  {
    if ( string.IsNullOrWhiteSpace( rawName ) )
    {
      throw new BadRequestException( "Counterparty name is required." ) ;
    }

    var trimmed = rawName.Trim() ;
    var lowered = trimmed.ToLowerInvariant() ;

    var existing = await repository.Query()
      .FirstOrDefaultAsync( c => c.AppUserId == userId && c.Name.ToLower() == lowered ) ;
    if ( existing is not null )
    {
      return existing ;
    }

    var entry = new Counterparty
    {
      AppUserId = userId,
      Name = trimmed
    } ;
    await repository.AddAsync( entry ) ;
    return entry ;
  }
}
