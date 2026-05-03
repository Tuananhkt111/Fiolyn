using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.EntityFrameworkCore ;
using Npgsql ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   CRUD over the admin-managed transaction-category lookup. Each row is bound to one
///   <see cref="TransactionKind" />; the unique index on <c>(Kind, Name)</c> prevents duplicates
///   within a kind but allows the same name across different kinds.
/// </summary>
public class TransactionCategoryService(
  IRepository<TransactionCategory> repository ) : ITransactionCategoryService
{
  /// <inheritdoc />
  public async Task<TransactionCategory> CreateAsync( string name, TransactionKind kind )
  {
    var trimmed = name.Trim() ;

    var existing = await repository.Query()
      .FirstOrDefaultAsync( c => c.Kind == kind && c.Name == trimmed ) ;
    if ( existing is not null )
    {
      throw new ConflictException( $"Category '{trimmed}' already exists for kind {kind}." ) ;
    }

    var entry = new TransactionCategory
    {
      Name = trimmed,
      Kind = kind
    } ;

    await repository.AddAsync( entry ) ;
    try
    {
      await repository.SaveAsync() ;
    }
    catch ( DbUpdateException ex )
      when ( ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } )
    {
      throw new ConflictException( $"Category '{trimmed}' already exists for kind {kind}." ) ;
    }

    return entry ;
  }

  /// <inheritdoc />
  public async Task<TransactionCategory> UpdateAsync( long id, string name )
  {
    var entry = await repository.GetByIdOrThrowAsync( id ) ;
    var trimmed = name.Trim() ;
    entry.Name = trimmed ;

    try
    {
      await repository.SaveAsync() ;
    }
    catch ( DbUpdateException ex )
      when ( ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } )
    {
      throw new ConflictException( $"Category '{trimmed}' already exists for kind {entry.Kind}." ) ;
    }

    return entry ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var entry = await repository.GetByIdOrThrowAsync( id ) ;
    repository.SoftDelete( entry ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task<List<TransactionCategory>> GetAllAsync( TransactionKind? kind = null )
  {
    var query = repository.Query().AsNoTracking() ;
    if ( kind is not null )
    {
      query = query.Where( c => c.Kind == kind.Value ) ;
    }

    return await query.OrderBy( c => c.Name ).ToListAsync() ;
  }

  /// <inheritdoc />
  public async Task<TransactionCategory?> GetByIdAsync( long id ) =>
    await repository.Query().FirstOrDefaultAsync( c => c.Id == id ) ;
}
