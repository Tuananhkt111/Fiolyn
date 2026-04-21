using Fiolyn.Data.Seeds ;
using Fiolyn.ServiceRegisters.Extensions ;

var builder = WebApplication.CreateBuilder( args ) ;

builder.Services.RegisterAllServices( builder.Configuration ) ;

var app = builder.Build() ;

await app.InitializeDatabaseAsync() ;

if ( app.Environment.IsDevelopment() )
{
  app.UseSwagger() ;
  app.UseSwaggerUI() ;
}

app.UseExceptionHandler() ;
app.UseCors() ;
app.UseAuthentication() ;
app.UseAuthorization() ;
app.MapControllers() ;

app.Run() ;
