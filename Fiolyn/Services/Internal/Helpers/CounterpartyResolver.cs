using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal.Helpers ;

/// <summary>
///   Resolves free-text counterparty name(s) to <see cref="Counterparty" /> rows owned by the
///   caller. Returns existing rows on case-insensitive match; otherwise stages new rows via
///   <c>AddAsync</c> (no save) so the leaf service's subsequent <c>SaveAsync</c> commits the
///   new counterparties and the transaction in the same transaction.
/// </summary>
internal static class CounterpartyResolver
{
  internal static async Task<Counterparty> ResolveOrCreateAsync(
    IRepository<Counterparty> repository, string rawName, long userId )
  {
    var batch = await ResolveOrCreateManyAsync( repository, [rawName], userId ) ;
    return batch.Values.Single() ;
  }

  /// <summary>
  ///   Single-round-trip variant for callers that have several names at once (e.g. share-list
  ///   resolution). Performs one <c>WHERE Name IN (...)</c> lookup, then stages new rows for
  ///   the misses. Returns a case-insensitive dictionary keyed by the *trimmed* input name —
  ///   duplicate-name dedup across the input list is the caller's responsibility.
  /// </summary>
  internal static async Task<IReadOnlyDictionary<string, Counterparty>> ResolveOrCreateManyAsync(
    IRepository<Counterparty> repository, IReadOnlyList<string> rawNames, long userId )
  {
    var trimmedByLowered = new Dictionary<string, string>() ;
    foreach ( var raw in rawNames )
    {
      if ( string.IsNullOrWhiteSpace( raw ) )
      {
        throw new BadRequestException( "Counterparty name is required." ) ;
      }

      var trimmed = raw.Trim() ;
      trimmedByLowered.TryAdd( trimmed.ToLowerInvariant(), trimmed ) ;
    }

    var loweredKeys = trimmedByLowered.Keys.ToHashSet() ;
    // Deleted contacts are included: the unique (AppUserId, LOWER(Name)) index covers them, so a
    // name that only a deleted contact holds is restored, not staged as a new row (ADR 0024).
    // The index allows one row per lowered name and user, so the dictionary keys are unique.
    var existing = ( await repository.Query()
        .IgnoreQueryFilters()
        .Where( c => c.AppUserId == userId && loweredKeys.Contains( c.Name.ToLower() ) )
        .ToListAsync() )
      .ToDictionary( c => c.Name.ToLowerInvariant(), c => c ) ;

    var result = new Dictionary<string, Counterparty>( StringComparer.OrdinalIgnoreCase ) ;
    foreach ( var (lowered, trimmed) in trimmedByLowered )
    {
      if ( existing.TryGetValue( lowered, out var found ) )
      {
        if ( found.IsDeleted )
        {
          repository.Restore( found ) ; // staged like an insert: the leaf's single save commits it
        }

        result[ trimmed ] = found ;
        continue ;
      }

      var fresh = new Counterparty
      {
        AppUserId = userId,
        Name = trimmed
      } ;
      await repository.AddAsync( fresh ) ;
      result[ trimmed ] = fresh ;
    }

    return result ;
  }
}
