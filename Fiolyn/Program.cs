using Fiolyn.Data ;
using Fiolyn.ServiceRegisters.Extensions ;
using Microsoft.EntityFrameworkCore ;

var builder = WebApplication.CreateBuilder( args ) ;

// Register services
builder.Services.RegisterAllServices( builder.Configuration ) ;

var app = builder.Build() ;

// Auto-apply migrations on startup
using ( var scope = app.Services.CreateScope() )
{
  var db = scope.ServiceProvider.GetRequiredService<AppDbContext>() ;
  db.Database.Migrate() ;
}

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