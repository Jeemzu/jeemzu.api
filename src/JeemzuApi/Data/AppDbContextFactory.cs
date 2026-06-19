using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JeemzuApi.Data;

/// <summary>
/// Allows EF Core CLI tools (dotnet ef migrations add, dotnet ef database update)
/// to instantiate AppDbContext at design time without a running app or real DB.
/// The connection string here is only used by the CLI — never at runtime.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=jeemzu_db;Username=jeemzu;Password=jeemzu_dev_password")
            .Options;

        return new AppDbContext(options);
    }
}
