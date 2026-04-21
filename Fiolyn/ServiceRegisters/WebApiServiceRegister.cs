using System.Reflection ;
using Fiolyn.Infrastructure ;
using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.ServiceRegisters.Settings ;
using Microsoft.OpenApi ;
using Newtonsoft.Json ;
using Newtonsoft.Json.Converters ;
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
      options.SerializerSettings.Converters.Add( new StringEnumConverter() ) ;
    } ) ;

    services.AddProblemDetails() ;
    services.AddExceptionHandler<GlobalExceptionHandler>() ;

    services.AddHttpClient() ;

    var corsSettings = configuration.GetSection( "Cors" ).Get<CorsSettings>() ?? new CorsSettings() ;
    services.AddCors( options =>
    {
      options.AddDefaultPolicy( policy =>
      {
        policy.WithOrigins( corsSettings.AllowedOrigins ).AllowAnyMethod().AllowAnyHeader() ;
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
