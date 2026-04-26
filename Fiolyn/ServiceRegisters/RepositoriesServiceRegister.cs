using Fiolyn.Data.Repositories ;
using Fiolyn.ServiceRegisters.Base ;

namespace Fiolyn.ServiceRegisters ;

/// <summary>
///   Service registrar for generic repository pattern.
/// </summary>
public class RepositoriesServiceRegister : IServiceRegister
{
  /// <summary>
  ///   Gets the execution order (runs after database registration).
  /// </summary>
  public int Order => 1 ;

  /// <summary>
  ///   Registers repository services.
  /// </summary>
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.AddScoped( typeof( IRepository<> ), typeof( Repository<> ) ) ;
    services.AddScoped<IUnitOfWork, UnitOfWork>() ;
  }
}
