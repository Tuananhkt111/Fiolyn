using Fiolyn.ServiceRegisters.Extensions ;

var builder = WebApplication.CreateBuilder( args ) ;

// Register services
builder.Services.RegisterAllServices( builder.Configuration ) ;

var app = builder.Build() ;

// Configure the HTTP request pipeline.
if ( app.Environment.IsDevelopment() )
{
  app.UseSwagger() ;
  app.UseSwaggerUI() ;
}

app.UseCors() ;
app.UseAuthentication() ;
app.UseAuthorization() ;
app.MapControllers() ;

app.Run() ;