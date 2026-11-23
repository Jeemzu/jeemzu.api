using System.Net;
using JeemzuApi.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace JeemzuApi.Tests;

public sealed class RetiredGamesEndpointTests
{
    [Theory]
    [InlineData("GET", "/api/scores/snake")]
    [InlineData("GET", "/api/scores/snake/summary")]
    [InlineData("POST", "/api/scores")]
    public async Task ScoreRoutesAreUnavailable(string method, string path)
    {
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
                services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly))
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
        using var client = server.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
