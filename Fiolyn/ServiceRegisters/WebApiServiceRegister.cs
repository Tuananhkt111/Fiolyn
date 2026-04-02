using System.Reflection ;
using Fiolyn.ServiceRegisters.Base ;
using Microsoft.OpenApi.Models ;
using Newtonsoft.Json ;

namespace Fiolyn.ServiceRegisters ;

public class WebApiServiceRegister : IServiceRegister
{
  #region Private readonly variables

  private const string SwaggerTitle = "Fiolyn API" ;

  #endregion

  public int Order => 0 ;

  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    // Controllers
    services.AddControllers().AddNewtonsoftJson( options =>
    {
      options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore ;
    } ) ;

    // HTTP Client Factory
    services.AddHttpClient() ;

    // Cors
    services.AddCors( options =>
    {
      options.AddDefaultPolicy( policy =>
      {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader() ;
      } ) ;
    } ) ;

    // Endpoints Api
    services.AddEndpointsApiExplorer() ;

    // Swagger
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