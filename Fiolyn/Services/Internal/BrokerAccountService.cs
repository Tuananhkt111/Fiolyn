using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Manages broker accounts.
/// </summary>
public class BrokerAccountService( IRepository<BrokerAccount> repository ) : IBrokerAccountService
{
  /// <inheritdoc />
  public async Task<BrokerAccount> CreateAsync( BrokerAccount brokerAccount )
  {
    await repository.AddAsync( brokerAccount ) ;
    await repository.SaveAsync() ;
    return brokerAccount ;
  }

  /// <inheritdoc />
  public async Task<List<BrokerAccount>> GetAllAsync() => await repository.QueryActive().OrderBy( b => b.Name ).ToListAsync() ;

  /// <inheritdoc />
  public async Task<bool> ExistsAsync( long id ) => await repository.QueryActive().AnyAsync( b => b.Id == id ) ;

  /// <inheritdoc />
  public async Task<BrokerAccount?> GetByIdAsync( long id )
    => await repository.GetActiveByIdAsync( id ) ;

  /// <inheritdoc />
  public async Task UpdateAsync( BrokerAccount brokerAccount )
  {
    repository.Update( brokerAccount ) ;
    await repository.SaveAsync() ;
  }

  /// <inheritdoc />
  public async Task SoftDeleteAsync( long id )
  {
    var brokerAccount = await repository.GetByIdAsync( id )
                        ?? throw new KeyNotFoundException( $"BrokerAccount {id} not found" ) ;
    repository.SoftDelete( brokerAccount ) ;
    await repository.SaveAsync() ;
  }
}