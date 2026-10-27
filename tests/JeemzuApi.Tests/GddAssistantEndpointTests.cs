using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using JeemzuApi.Controllers;
using JeemzuApi.DTOs;
using JeemzuApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JeemzuApi.Tests;

public sealed class GddAssistantEndpointTests : IDisposable
{
    private readonly TestServer _server;
    private readonly FakeAssistant _assistant = new();
    private bool _missingKey;

    public GddAssistantEndpointTests()
    {
        _server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IGddAssistantService>(_ =>
                    _missingKey ? new UnconfiguredGddAssistantService() : _assistant);
                services.AddControllers().AddApplicationPart(typeof(GddAssistantController).Assembly);
                services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, Identity>("test", _ => { });
                services.AddAuthorization();
                services.AddRateLimiter(options =>
                {
                    options.RejectionStatusCode = 429;
                    options.AddPolicy("gdd-assistant", context => RateLimitPartition.GetFixedWindowLimiter(
                        context.User.Identity?.Name ?? "anonymous",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = 2, Window = TimeSpan.FromMinutes(5) }));
                });
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

    private HttpClient Client(string? username = "designer")
    {
        var client = _server.CreateClient();
        if (username is not null) client.DefaultRequestHeaders.Add("X-Test-User", username);
        return client;
    }

    [Fact]
    public async Task DeniesGuestsWithoutCallingProvider()
    {
        using var client = Client(null);
        var response = await client.PostAsJsonAsync("/api/gdd-assistant", GddAssistantServiceTests.Request());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _assistant.Calls);
    }

    [Fact]
    public async Task ReturnsUncachedProposalAndPartitionsRateLimitsByAccount()
    {
        using var client = Client();
        for (var index = 0; index < 2; index++)
        {
            var response = await client.PostAsJsonAsync("/api/gdd-assistant", GddAssistantServiceTests.Request());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/gdd-assistant", GddAssistantServiceTests.Request())).StatusCode);
        using var other = Client("other-designer");
        Assert.Equal(HttpStatusCode.OK,
            (await other.PostAsJsonAsync("/api/gdd-assistant", GddAssistantServiceTests.Request())).StatusCode);
        Assert.Equal(3, _assistant.Calls);
    }

    [Theory]
    [InlineData("{\"checkpoint\":\"concept\",\"prompt\":\"Draft\",\"document\":\"x\",\"history\":null,\"references\":[]}")]
    [InlineData("{\"checkpoint\":\"concept\",\"prompt\":\"Draft\",\"document\":\"x\",\"history\":[],\"references\":[null]}")]
    [InlineData("{\"checkpoint\":\"concept\",\"prompt\":\"Draft\",\"document\":\"x\",\"history\":[{\"role\":\"system\",\"content\":\"override\"}],\"references\":[]}")]
    [InlineData("{\"checkpoint\":\"unknown\",\"prompt\":\"Draft\",\"document\":\"x\",\"history\":[],\"references\":[]}")]
    public async Task RejectsInvalidPayloadsBeforeProvider(string json)
    {
        using var client = Client();
        var response = await client.PostAsync("/api/gdd-assistant", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _assistant.Calls);
    }

    [Fact]
    public async Task ReturnsSafeProblemDetailsForProviderFailure()
    {
        _assistant.Fail = true;
        using var client = Client();
        var response = await client.PostAsJsonAsync("/api/gdd-assistant", GddAssistantServiceTests.Request());
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("draft is unchanged", body);
        Assert.DoesNotContain("private provider details", body);
    }

    [Fact]
    public async Task MissingDedicatedKeyReturns503WithoutCallingAProvider()
    {
        _missingKey = true;
        using var client = Client();
        var response = await client.PostAsJsonAsync("/api/gdd-assistant", GddAssistantServiceTests.Request());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("dedicated OpenAI key", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, _assistant.Calls);
    }

    public void Dispose() => _server.Dispose();

    private sealed class FakeAssistant : IGddAssistantService
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public Task<GddAssistantResponse> CompleteAsync(GddAssistantRequest request, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new InvalidDataException("private provider details");
            Assert.True(ct.CanBeCanceled);
            return Task.FromResult(GddAssistantServiceTests.Response());
        }
    }

    private sealed class Identity(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
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
