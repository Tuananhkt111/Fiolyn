using Fiolyn.ServiceRegisters.Base ;

namespace Fiolyn.ServiceRegisters.Extensions ;

public static class ServiceRegistrarExtensions
{
  public static void RegisterAllServices( this IServiceCollection services, IConfiguration configuration )
  {
    var registrars = typeof( IServiceRegister ).Assembly
      .GetTypes()
      .Where( t => typeof( IServiceRegister ).IsAssignableFrom( t ) && ! t.IsInterface && ! t.IsAbstract )
      .Where( t => t.GetConstructor( Type.EmptyTypes ) != null )
      .Select( t => ( IServiceRegister )Activator.CreateInstance( t )! )
      .OrderBy( r => r.Order ) ;

    foreach ( var registrar in registrars )
    {
      registrar.RegisterServices( services, configuration ) ;
    }
  }
}
