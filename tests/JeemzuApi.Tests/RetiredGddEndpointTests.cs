using System.Net;
using JeemzuApi.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace JeemzuApi.Tests;

public sealed class RetiredGddEndpointTests
{
    [Theory]
    [InlineData("POST", "/api/gdd-assistant")]
    [InlineData("POST", "/api/gdd-assistant/guided")]
    [InlineData("GET", "/api/gdd-documents")]
    [InlineData("POST", "/api/gdd-documents")]
    [InlineData("GET", "/api/gdd-documents/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/gdd-documents/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/gdd-documents/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/gdd-documents/00000000-0000-0000-0000-000000000001/pdf")]
    [InlineData("GET", "/api/gdd-documents/00000000-0000-0000-0000-000000000001/draft")]
    public async Task GddRoutesAreUnavailable(string method, string path)
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
