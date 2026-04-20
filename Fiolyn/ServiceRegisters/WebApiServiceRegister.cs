using System.Reflection ;
using Fiolyn.Infrastructure ;
using Fiolyn.ServiceRegisters.Base ;
using Microsoft.OpenApi ;
using Newtonsoft.Json ;
using Newtonsoft.Json.Serialization ;

namespace Fiolyn.ServiceRegisters ;

public class WebApiServiceRegister : IServiceRegister
{
  private const string SwaggerTitle = "Fiolyn API" ;

  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.AddControllers().AddNewtonsoftJson( options =>
    {
      options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver() ;
      options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore ;
    } ) ;

    services.AddProblemDetails() ;
    services.AddExceptionHandler<GlobalExceptionHandler>() ;

    services.AddHttpClient() ;

    services.AddCors( options =>
    {
      options.AddDefaultPolicy( policy =>
      {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader() ;
      } ) ;
    } ) ;

    services.AddEndpointsApiExplorer() ;

    services.AddSwaggerGen( o =>
    {
      o.SwaggerDoc( "v1", new OpenApiInfo
      {
        Title = SwaggerTitle,
        Version = "v1"
      } ) ;

      var xmlFileName = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml" ;
      var xmlPath = Path.Combine( AppContext.BaseDirectory, xmlFileName ) ;
      if ( File.Exists( xmlPath ) )
      {
        o.IncludeXmlComments( xmlPath ) ;
      }
    } ) ;
  }
}