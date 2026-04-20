using Fiolyn.Data.Seeds ;
using Fiolyn.ServiceRegisters.Extensions ;

var builder = WebApplication.CreateBuilder( args ) ;

// Register services
builder.Services.RegisterAllServices( builder.Configuration ) ;

var app = builder.Build() ;

// Apply EF Core migrations and run the initial-admin seeder.
await app.InitializeDatabaseAsync() ;

// Configure the HTTP request pipeline.
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