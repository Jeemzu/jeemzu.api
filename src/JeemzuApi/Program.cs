using JeemzuApi.Data;
using JeemzuApi.Hubs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Resend;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllers();

// Swagger / OpenAPI — available in all environments for now; restrict to Development later
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "JeemzuAPI", Version = "v1" });

    // Allow sending JWT bearer tokens from the Swagger UI
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT access token."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// EF Core — Npgsql (PostgreSQL)
// Connection string comes from:
//   Development : dotnet user-secrets ("ConnectionStrings:DefaultConnection")
//   Production  : Azure App Service environment variable
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseVector()));

// Application services — Scoped so they share the DbContext per request
builder.Services.AddScoped<IScoreService, ScoreService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Resend email service — used by POST /api/contact
var resendApiKey = builder.Configuration["Resend:ApiKey"]
    ?? throw new InvalidOperationException(
        "Resend:ApiKey is not configured. Set it via: dotnet user-secrets set \"Resend:ApiKey\" \"<key>\"");
builder.Services.AddResend(options => { options.ApiToken = resendApiKey; });
builder.Services.AddScoped<IEmailService, EmailService>();

// RPG multiplayer — SignalR for real-time party/gameplay, Party service for lobby
// management, Turn service for combat turn validation/timeouts, and an HTTP proxy
// to the separate Python LangGraph RPG orchestration service.
builder.Services.AddSignalR();
builder.Services.AddScoped<IPartyService, PartyService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddSingleton<ITurnService, TurnService>();

var rpgServiceUrl = builder.Configuration["Rpg:ServiceUrl"] ?? "http://localhost:8001";
builder.Services.AddHttpClient<IRpgProxyService, RpgProxyService>(client =>
{
    client.BaseAddress = new Uri(rpgServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ── Semantic Kernel + OpenAI ─────────────────────────────────────────────────
// The Kernel and the LLM/embedding service instances are singletons: they hold
// no per-request state and the underlying HTTP clients are designed to be reused.
var openAiApiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured. Set it via: dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\"");
var chatModel = builder.Configuration["OpenAI:ChatModel"] ?? "gpt-4o-mini";
var embeddingModel = builder.Configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";

#pragma warning disable SKEXP0010 // AddOpenAIEmbeddingGenerator is experimental in SK 1.x but stable in practice
var kernel = Kernel.CreateBuilder()
    .AddOpenAIChatCompletion(chatModel, openAiApiKey)
    .AddOpenAIEmbeddingGenerator(embeddingModel, openAiApiKey)
    .Build();
#pragma warning restore SKEXP0010

builder.Services.AddSingleton(kernel);
builder.Services.AddSingleton(kernel.Services.GetRequiredService<IChatCompletionService>());
builder.Services.AddSingleton(kernel.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());

// RAG services — Scoped so they share the DbContext per request
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IVectorStoreService, VectorStoreService>();
builder.Services.AddScoped<IIngestionService, IngestionService>();
builder.Services.AddScoped<IChatService, ChatService>();

// JWT authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "jeemzu-api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "jeemzu-frontend";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };

        // SignalR WebSocket/SSE connections can't set an Authorization header, so the
        // JS client sends the access token via query string instead — extract it here.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Health checks — includes a DB connectivity check via Npgsql
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString);

// CORS — allow the Netlify frontend and the local Vite dev server
builder.Services.AddCors(options =>
{
    options.AddPolicy("JeemzuFrontend", policy =>
    {
        policy.WithOrigins(
                "https://jeemzu.me",
                "https://www.jeemzu.me",
                "http://localhost:5173",  // Vite default port
                "http://localhost:8001"   // Python agent service
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();   // Required for httpOnly refresh token cookie
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
app.UseAuthentication();
app.UseAuthorization();

// Lightweight health endpoint — useful for Azure App Service health probes
app.MapHealthChecks("/health");

app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();
