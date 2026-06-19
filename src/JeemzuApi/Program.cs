using JeemzuApi.Data;
using JeemzuApi.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllers();

// Swagger / OpenAPI — available in all environments for now; restrict to Development later
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Jeemzu API", Version = "v1" });
});

// EF Core — Npgsql (PostgreSQL)
// Connection string comes from:
//   Development : dotnet user-secrets ("ConnectionStrings:DefaultConnection")
//   Production  : Azure App Service environment variable
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Application services — Scoped so they share the DbContext per request
builder.Services.AddScoped<IScoreService, ScoreService>();
builder.Services.AddScoped<IUserService, UserService>();

// Health checks — includes a DB connectivity check via Npgsql
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString);

// CORS — allow the Netlify frontend and the local Vite dev server
builder.Services.AddCors(options =>
{
    options.AddPolicy("JeemzuFrontend", policy =>
    {
        policy.WithOrigins(
                "https://jeemzu.com",
                "https://www.jeemzu.com",
                "http://localhost:5173"   // Vite default port
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// ── App pipeline ─────────────────────────────────────────────────────────────

var app = builder.Build();

// Auto-apply any pending EF migrations at startup.
// This means a fresh Azure deploy automatically creates/updates the schema.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("JeemzuFrontend");
app.UseAuthorization();

// Lightweight health endpoint — useful for Azure App Service health probes
app.MapHealthChecks("/health");

app.MapControllers();

app.Run();
