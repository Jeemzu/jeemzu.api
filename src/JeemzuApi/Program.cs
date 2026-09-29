using JeemzuApi.Data;
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
using System.Threading.RateLimiting;

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
//   Production  : Render environment variable, in Npgsql keyword form (not a postgres:// URI)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseVector()));

// Application services — Scoped so they share the DbContext per request
builder.Services.AddScoped<IScoreService, ScoreService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAccountService, AccountService>();

// Resend email service — used by POST /api/contact and the account verification/reset emails.
// Key is optional at startup; EmailService will return 503 if unconfigured.
builder.Services.AddResend(options =>
{
    options.ApiToken = builder.Configuration["Resend:ApiKey"] ?? string.Empty;
});
builder.Services.AddScoped<IEmailService, EmailService>();

// Throttle account recovery endpoints so password guesses and reset emails can't be ground through.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Signed-in actions partition by username, so one user can never throttle another.
    options.AddPolicy("account-user", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.Identity?.Name ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
        }));

    // Anonymous recovery endpoints can only partition by IP. Behind a reverse proxy that
    // may group callers together, so the limit is loose enough to avoid locking out real
    // users — token entropy, not this limiter, is what makes the links unguessable.
    options.AddPolicy("account-public", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(5),
        }));
});

// Python agent service — one FastAPI app serving both the chatbot and the Budgetize
// assistant. The planner node can take a while on a large budget, so the timeout is generous.
var agentsBaseUrl = builder.Configuration["Agents:BaseUrl"] ?? "http://localhost:8001";
builder.Services.AddHttpClient<IBudgetAgentProxyService, BudgetAgentProxyService>(client =>
{
    client.BaseAddress = new Uri(agentsBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
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

// Apply pending EF migrations, then seed, so a fresh deploy comes up ready to serve.
// Render's managed Postgres can refuse the first connection or two while a newly deployed
// container starts, hence the bounded retry. A retrying EF execution strategy is deliberately
// NOT used: it forbids the user-initiated transaction in AccountService.ChangeUsernameAsync.
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var startupLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    const int maxAttempts = 5;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await sp.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            await DbSeeder.SeedAsync(sp);
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            var delay = TimeSpan.FromSeconds(2 * attempt);
            startupLogger.LogWarning(ex,
                "Database startup attempt {Attempt}/{Max} failed; retrying in {Delay}s.",
                attempt, maxAttempts, delay.TotalSeconds);
            await Task.Delay(delay);
        }
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("JeemzuFrontend");
app.UseAuthentication();
// After authentication so the "account-user" policy can partition by username.
app.UseRateLimiter();
app.UseAuthorization();

// Lightweight health endpoint — also used as the Render health check path
app.MapHealthChecks("/health");

app.MapControllers();

app.Run();
