using Fiolyn.Data ;
using Fiolyn.Services ;
using Microsoft.EntityFrameworkCore ;

var builder = WebApplication.CreateBuilder( args ) ;

// Database
builder.Services.AddDbContext<AppDbContext>( options =>
  options.UseNpgsql( Environment.GetEnvironmentVariable( "DATABASE_URL" ) ) ) ;

// Notion
builder.Services.AddSingleton( sp => new NotionService(
  Environment.GetEnvironmentVariable( "NOTION_API_KEY" )!,
  Environment.GetEnvironmentVariable( "NOTION_DATABASE_ID" )!,
  sp.GetRequiredService<ILogger<NotionService>>()
) ) ;

// Add services to the container.

builder.Services.AddControllers() ;
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer() ;
builder.Services.AddSwaggerGen() ;

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

app.MapControllers() ;

app.Run() ;