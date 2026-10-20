using System.Text;
using JeemzuApi.Data;
using JeemzuApi.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JeemzuApi.Services;

internal static class LocalAdminPasswordReset
{
    public static async Task<int> RunAsync(IConfiguration configuration, string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
            return Fail("Usage: --reset-local-admin-password <username>");
        if (Console.IsInputRedirected)
            return Fail("Run this command in an interactive terminal. Password input must not be redirected.");

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            return Fail("Set ConnectionStrings__DefaultConnection to your local Docker PostgreSQL connection.");
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (connection.Host is not ("localhost" or "127.0.0.1" or "::1") || connection.Database != "jeemzu_db")
            return Fail("Reset refused: only a loopback connection to the local jeemzu_db database is allowed.");

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.UseVector()).Options);
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Username == args[1] && candidate.Role == "Admin");
        if (user is null)
            return Fail("No local admin with that username exists. No changes made.");

        Console.WriteLine($"Resetting local admin '{user.Username}' in {connection.Host}:{connection.Port}/{connection.Database}.");
        var password = ReadPassword("New password: ");
        if (password.Length is < 8 or > 100 || Encoding.UTF8.GetByteCount(password) > 72)
            return Fail("Use 8-100 characters and at most 72 UTF-8 bytes for BCrypt. No changes made.");
        if (password != ReadPassword("Confirm password: "))
            return Fail("Passwords do not match. No changes made.");

        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var rows = await db.Users.Where(candidate => candidate.Id == user.Id && candidate.Role == "Admin")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.PasswordHash, hash)
                .SetProperty(candidate => candidate.UpdatedAt, now));
        if (rows != 1)
            return Fail("The admin account changed during reset. No changes made.");
        await db.RefreshTokens.Where(token => token.Username == user.Username && !token.IsRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.IsRevoked, true));
        await db.UserTokens.Where(token => token.UserId == user.Id &&
                token.Purpose == UserTokenPurpose.PasswordReset && token.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ConsumedAt, now));
        await transaction.CommitAsync();

        var storedHash = await db.Users.AsNoTracking().Where(candidate => candidate.Id == user.Id)
            .Select(candidate => candidate.PasswordHash).SingleAsync();
        if (storedHash is null || !BCrypt.Net.BCrypt.Verify(password, storedHash))
            throw new InvalidOperationException("Password verification after reset failed.");
        Console.WriteLine("Local admin password reset and verified. Refresh sessions and reset links were revoked. Sign in again.");
        return 0;
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }
            if (key.Key == ConsoleKey.Escape)
                throw new OperationCanceledException("Password reset cancelled. No changes made.");
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0) password.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                if (password.Length >= 100)
                    throw new ArgumentException("Password exceeds 100 characters. No changes made.");
                password.Append(key.KeyChar);
            }
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
