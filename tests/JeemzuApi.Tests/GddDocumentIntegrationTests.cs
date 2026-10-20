using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using JeemzuApi.Controllers;
using JeemzuApi.Data;
using JeemzuApi.DTOs;
using JeemzuApi.Models;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace JeemzuApi.Tests;

public sealed class GddDatabase : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = "";
    public TestServer Server { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable("GDD_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Set GDD_TEST_CONNECTION to a disposable local gdd_test database.");
        var connection = new NpgsqlConnectionStringBuilder(ConnectionString);
        if (connection.Host is not ("localhost" or "127.0.0.1") || !connection.Database!.StartsWith("gdd_test"))
            throw new InvalidOperationException("Integration tests require a disposable local gdd_test database.");
        await using var db = Context();
        await db.Database.MigrateAsync();
        Server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(ConnectionString, postgres => postgres.UseVector()));
                services.AddScoped<IGddDocumentService, GddDocumentService>();
                services.AddControllers().AddApplicationPart(typeof(GddDocumentsController).Assembly);
                services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestIdentity>("test", _ => { });
                services.AddAuthorization();
                services.AddRateLimiter(options => options.AddPolicy("account-user", _ =>
                    System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")));
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseRateLimiter();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
    }

    public AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString, postgres => postgres.UseVector()).Options);

    public async Task<(Guid Id, HttpClient Client)> UserAsync()
    {
        var user = new User { Username = $"test-{Guid.NewGuid():N}" };
        await using var db = Context();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var client = Server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", user.Username);
        return (user.Id, client);
    }

    public Task DisposeAsync() { Server.Dispose(); NpgsqlConnection.ClearAllPools(); return Task.CompletedTask; }

    private sealed class TestIdentity(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var username = Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrEmpty(username)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

public sealed class GddDocumentIntegrationTests(GddDatabase database) : IClassFixture<GddDatabase>
{
    private static MultipartFormDataContent Upload(string title = "Game", Guid? revision = null, byte[]? draft = null)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(title), "title" },
            { new ByteArrayContent(GddUploadValidatorTests.Pdf), "pdf", "game.pdf" },
            { new ByteArrayContent(draft ?? GddUploadValidatorTests.Draft(title)), "draft", "game.zip" },
        };
        if (revision is not null) content.Add(new StringContent(revision.Value.ToString()), "revision");
        return content;
    }

    [Fact]
    public async Task CreatesListsDownloadsUpdatesAndDeletesActualOwnedBytes()
    {
        var (_, client) = await database.UserAsync();
        var originalDraft = GddUploadValidatorTests.Draft();
        using var create = await client.PostAsync("/api/gdd-documents", Upload(draft: originalDraft));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        Assert.True(create.Headers.CacheControl?.NoStore);
        var saved = (await create.Content.ReadFromJsonAsync<GddDocumentResponse>())!;
        var library = (await client.GetFromJsonAsync<GddLibraryResponse>("/api/gdd-documents"))!;
        Assert.Equal(GddUploadValidator.AccountLimit, library.LimitBytes);
        Assert.Equal(saved.PdfBytes + saved.DraftBytes, library.UsedBytes);
        Assert.Equal(GddUploadValidatorTests.Pdf, await client.GetByteArrayAsync($"/api/gdd-documents/{saved.Id}/pdf?revision={saved.Revision}"));
        Assert.Equal(originalDraft, await client.GetByteArrayAsync($"/api/gdd-documents/{saved.Id}/draft?revision={saved.Revision}"));
        using var update = await client.PutAsync($"/api/gdd-documents/{saved.Id}", Upload("Updated", saved.Revision));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var newer = (await update.Content.ReadFromJsonAsync<GddDocumentResponse>())!;
        Assert.NotEqual(saved.Revision, newer.Revision);
        using var stale = await client.PutAsync($"/api/gdd-documents/{saved.Id}", Upload("Stale", saved.Revision));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/api/gdd-documents/{saved.Id}/pdf?revision={saved.Revision}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/gdd-documents/{saved.Id}?revision={saved.Revision}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/gdd-documents/{saved.Id}?revision={newer.Revision}")).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<GddLibraryResponse>("/api/gdd-documents"))!.UsedBytes);
    }

    [Fact]
    public async Task DeniesGuestsAndOtherUsersForEveryDocumentSurface()
    {
        var (_, owner) = await database.UserAsync();
        var (_, other) = await database.UserAsync();
        var saved = (await (await owner.PostAsync("/api/gdd-documents", Upload())).Content.ReadFromJsonAsync<GddDocumentResponse>())!;
        var guest = database.Server.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/gdd-documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsync("/api/gdd-documents", Upload())).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<GddLibraryResponse>("/api/gdd-documents"))!.Documents);
        foreach (var path in new[] { $"{saved.Id}", $"{saved.Id}/pdf?revision={saved.Revision}", $"{saved.Id}/draft?revision={saved.Revision}" })
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/gdd-documents/{path}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync($"/api/gdd-documents/{saved.Id}", Upload("Game", saved.Revision))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/gdd-documents/{saved.Id}?revision={saved.Revision}")).StatusCode);
    }

    [Fact]
    public async Task SerializesConcurrentUploadsSoQuotaCannotBeOverspent()
    {
        var (userId, _) = await database.UserAsync();
        await using (var seed = database.Context())
        {
            // Simulate existing charged storage without allocating a 97 MB fixture.
            seed.GddDocuments.Add(new GddDocument
            {
                Id = Guid.NewGuid(), UserId = userId, Title = "Existing", Revision = Guid.NewGuid(),
                PdfBytes = 97L * 1024 * 1024, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }
        async Task<GddSaveResult> Save()
        {
            await using var db = database.Context();
            return await new GddDocumentService(db).SaveAsync(userId, Guid.NewGuid(), "New",
                new byte[1024 * 1024], new byte[1024 * 1024], null, true, CancellationToken.None);
        }
        var results = await Task.WhenAll(Save(), Save());
        Assert.Single(results, result => result.Status == GddSaveStatus.Saved);
        Assert.Single(results, result => result.Status == GddSaveStatus.QuotaExceeded);
        await using var final = database.Context();
        Assert.Equal(99L * 1024 * 1024, (await new GddDocumentService(final).ListAsync(userId, CancellationToken.None)).UsedBytes);
    }

    [Fact]
    public async Task AccountDeletionCascadesDocuments()
    {
        var (userId, client) = await database.UserAsync();
        var saved = await client.PostAsync("/api/gdd-documents", Upload());
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        await using var db = database.Context();
        await db.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
        Assert.False(await db.GddDocuments.AnyAsync(document => document.UserId == userId));
    }

    [Fact]
    public async Task AcceptsExactQuotaRejectsOneByteOverAndCreditsReplacedBytes()
    {
        var (userId, _) = await database.UserAsync();
        var existingId = Guid.NewGuid();
        var existingRevision = Guid.NewGuid();
        await using var db = database.Context();
        db.GddDocuments.Add(new GddDocument
        {
            Id = existingId, UserId = userId, Title = "Existing", Revision = existingRevision,
            PdfBytes = GddUploadValidator.AccountLimit - 2,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var service = new GddDocumentService(db);
        var exact = await service.SaveAsync(userId, Guid.NewGuid(), "Exact", [1], [2], null, true, CancellationToken.None);
        Assert.Equal(GddSaveStatus.Saved, exact.Status);
        var over = await service.SaveAsync(userId, Guid.NewGuid(), "Over", [1], [], null, true, CancellationToken.None);
        Assert.Equal(GddSaveStatus.QuotaExceeded, over.Status);
        var replaced = await service.SaveAsync(userId, existingId, "Smaller", [1], [2], existingRevision, false, CancellationToken.None);
        Assert.Equal(GddSaveStatus.Saved, replaced.Status);
        Assert.Equal(4, (await service.ListAsync(userId, CancellationToken.None)).UsedBytes);
    }

    [Fact]
    public async Task ConcurrentUpdatesAtOneRevisionHaveExactlyOneWinner()
    {
        var (userId, client) = await database.UserAsync();
        var saved = (await (await client.PostAsync("/api/gdd-documents", Upload())).Content.ReadFromJsonAsync<GddDocumentResponse>())!;
        async Task<GddSaveResult> Update(string title)
        {
            await using var db = database.Context();
            return await new GddDocumentService(db).SaveAsync(userId, saved.Id, title,
                GddUploadValidatorTests.Pdf, GddUploadValidatorTests.Draft(title), saved.Revision, false, CancellationToken.None);
        }
        var results = await Task.WhenAll(Update("First"), Update("Second"));
        Assert.Single(results, result => result.Status == GddSaveStatus.Saved);
        Assert.Single(results, result => result.Status == GddSaveStatus.Conflict);
    }

    [Fact]
    public async Task RejectsInvalidMultipartAndMissingRevisionsWithoutSaving()
    {
        var (_, client) = await database.UserAsync();
        using var missing = new MultipartFormDataContent { { new StringContent("Game"), "title" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/gdd-documents", missing)).StatusCode);
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync($"/api/gdd-documents/{id}", Upload())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/gdd-documents/{id}/pdf")).StatusCode);
        using var mismatch = new MultipartFormDataContent
        {
            { new StringContent("Different title"), "title" },
            { new ByteArrayContent(GddUploadValidatorTests.Pdf), "pdf", "game.pdf" },
            { new ByteArrayContent(GddUploadValidatorTests.Draft()), "draft", "game.zip" },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/gdd-documents", mismatch)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<GddLibraryResponse>("/api/gdd-documents"))!.Documents);
    }
}
