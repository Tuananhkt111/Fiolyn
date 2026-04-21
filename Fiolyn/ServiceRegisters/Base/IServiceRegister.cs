namespace Fiolyn.ServiceRegisters.Base ;

public interface IServiceRegister
{
  int Order => 0 ;
  void RegisterServices( IServiceCollection services, IConfiguration configuration ) ;
}
