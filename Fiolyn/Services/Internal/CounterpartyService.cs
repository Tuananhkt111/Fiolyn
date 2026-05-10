using Fiolyn.Common.Pagination ;
using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;
using Npgsql ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="ICounterpartyService" />
public class CounterpartyService(
  IRepository<Counterparty> repository,
  IRepository<LendTransaction> lendRepository,
  IRepository<BorrowingTransaction> borrowingRepository ) : ICounterpartyService
{
  /// <inheritdoc />
  public async Task<PagedResult<Counterparty>> GetAllForUserAsync( long userId, PaginationQuery pagination ) =>
    await repository.Query()
      .AsNoTracking()
      .Where( c => c.AppUserId == userId )
      .OrderBy( c => c.Name )
      .ThenBy( c => c.Id )
      .ToPagedResultAsync( pagination ) ;

  /// <inheritdoc />
  public async Task<Counterparty?> GetByIdForUserAsync( long id, long userId ) =>
    await repository.Query()
      .FirstOrDefaultAsync( c => c.Id == id && c.AppUserId == userId ) ;

  /// <inheritdoc />
  public async Task<Counterparty> CreateAsync( string name, long userId )
  {
    if ( string.IsNullOrWhiteSpace( name ) )
    {
      throw new BadRequestException( "Counterparty name is required." ) ;
    }

    var trimmed = name.Trim() ;
    var lowered = trimmed.ToLowerInvariant() ;

    var existing = await repository.Query()
      .FirstOrDefaultAsync( c => c.AppUserId == userId && c.Name.ToLower() == lowered ) ;
    if ( existing is not null )
    {
      throw new ConflictException( $"Counterparty '{trimmed}' already exists." ) ;
    }

    var entry = new Counterparty
    {
      AppUserId = userId,
      Name = trimmed
    } ;
    await repository.AddAsync( entry ) ;
    try
    {
      await repository.SaveAsync() ;
    }
    catch ( DbUpdateException ex )
      when ( ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } )
    {
      throw new ConflictException( $"Counterparty '{trimmed}' already exists." ) ;
    }

    return entry ;
  }

  /// <inheritdoc />
  public async Task<Counterparty?> UpdateAsync( long id, long userId, string name )
  {
    if ( string.IsNullOrWhiteSpace( name ) )
    {
      throw new BadRequestException( "Counterparty name is required." ) ;
    }

    var entry = await GetByIdForUserAsync( id, userId ) ;
    if ( entry is null )
    {
      return null ;
    }

    var trimmed = name.Trim() ;
    var lowered = trimmed.ToLowerInvariant() ;

    var collision = await repository.Query()
      .AnyAsync( c => c.AppUserId == userId
                      && c.Id != id
                      && c.Name.ToLower() == lowered ) ;
    if ( collision )
    {
      throw new ConflictException( $"Counterparty '{trimmed}' already exists." ) ;
    }

    entry.Name = trimmed ;
    try
    {
      await repository.SaveAsync() ;
    }
    catch ( DbUpdateException ex )
      when ( ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } )
    {
      throw new ConflictException( $"Counterparty '{trimmed}' already exists." ) ;
    }

    return entry ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id, long userId )
  {
    var entry = await GetByIdForUserAsync( id, userId )
                ?? throw new KeyNotFoundException( $"Counterparty {id} not found for user {userId}." ) ;

    await EnsureNotReferencedByActiveTransactionAsync( id ) ;

    repository.SoftDelete( entry ) ;
    await repository.SaveAsync() ;
  }

  private async Task EnsureNotReferencedByActiveTransactionAsync( long counterpartyId )
  {
    var lendRefs = await lendRepository.Query()
      .AnyAsync( t => t.CounterpartyId == counterpartyId ) ;
    if ( lendRefs )
    {
      throw new BadRequestException(
        $"Counterparty {counterpartyId} is referenced by one or more active lend transactions — delete or reassign them first." ) ;
    }

    var borrowingRefs = await borrowingRepository.Query()
      .AnyAsync( t => t.CounterpartyId == counterpartyId ) ;
    if ( borrowingRefs )
    {
      throw new BadRequestException(
        $"Counterparty {counterpartyId} is referenced by one or more active borrowing transactions — delete or reassign them first." ) ;
    }
  }
}
