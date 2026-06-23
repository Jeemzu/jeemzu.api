using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Pgvector.EntityFrameworkCore;

namespace JeemzuApi.Data;

/// <summary>
/// Allows EF Core CLI tools (dotnet ef migrations add, dotnet ef database update)
/// to instantiate AppDbContext at design time without a running app.
/// Reads the connection string from user-secrets and environment variables so the
/// CLI uses the same Azure database as the running application.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<AppDbContextFactory>()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' not found. " +
                "Set it via: dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"<connection string>\"");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, o => o.UseVector())
            .Options;

        return new AppDbContext(options);
    }
}
