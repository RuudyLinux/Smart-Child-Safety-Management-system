using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;

// Every test class gets its own database, but the API reads its configuration from
// process environment variables, so classes must not build hosts concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ChildGuard.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API (in-process TestServer) against a brand-new, throwaway MySQL /
/// MariaDB database created for this test class and dropped afterwards.
///
/// Isolation guarantees — the factory refuses to run otherwise:
///  * The database name is always generated as "childguard_it_&lt;random&gt;"; it is never
///    read from configuration, so a test can never point at childguard_db or production.
///  * The server must be loopback unless CHILDGUARD_IT_ALLOW_REMOTE=1.
///  * After start-up, the connection the API actually uses is checked to be that database.
///
/// Server credentials: CHILDGUARD_IT_MYSQL (no database name), default
/// "Server=127.0.0.1;Port=3306;User=root;Password=;" (XAMPP default).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DatabasePrefix = "childguard_it_";
    public const string AdminEmail = "it-admin@childguard.test";

    private readonly string _serverConnectionString;

    public ApiFactory()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable("CHILDGUARD_IT_MYSQL")
            ?? "Server=127.0.0.1;Port=3306;User=root;Password=;";
        DatabaseName = DatabasePrefix + Guid.NewGuid().ToString("N")[..12];
        ConnectionString = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = DatabaseName }.ConnectionString;
        AdminPassword = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)) + "!a";
        JwtSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
    }

    public string DatabaseName { get; }
    public string ConnectionString { get; }
    public string AdminPassword { get; }
    public string JwtSecret { get; }

    /// <summary>The API's clock. Defaults to 10:00 at the school (Asia/Kolkata) on 6 Nov 2026.</summary>
    public TestTimeProvider Time { get; } = new(new DateTime(2026, 11, 6, 4, 30, 0, DateTimeKind.Utc));

    public FakeFaceServiceClient Face { get; } = new();

    /// <summary>Configuration overrides; subclasses can tighten e.g. rate limits.</summary>
    protected virtual IDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["ConnectionStrings__ChildGuardDb"] = ConnectionString,
        ["Jwt__Secret"] = JwtSecret,
        ["SeedAdmin__Email"] = AdminEmail,
        ["SeedAdmin__Password"] = AdminPassword,
        ["SeedAdmin__FullName"] = "Integration Test Admin",
        ["SeedDemoData"] = "false",
        // Never send real email or push from tests, whatever the developer's user-secrets hold.
        ["Email__Resend__ApiKey"] = "",
        ["Firebase__CredentialsPath"] = "",
        ["Rfid__DeviceApiKey"] = "",
        ["School__TimeZone"] = "Asia/Kolkata",
        ["RateLimiting__AuthPerMinute"] = "100000",
        ["RateLimiting__OtpPerMinute"] = "100000",
        ["RateLimiting__RfidScanPerMinute"] = "100000",
    };

    public async Task InitializeAsync()
    {
        GuardIsolation();

        await using (var server = new MySqlConnection(_serverConnectionString))
        {
            await server.OpenAsync();
            await using var create = server.CreateCommand();
            create.CommandText = $"CREATE DATABASE `{DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
            await create.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<ChildGuardDbContext>()
            .UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString)).Options;
        await using (var db = new ChildGuardDbContext(options))
            await db.Database.MigrateAsync();

        foreach (var (key, value) in Settings)
            Environment.SetEnvironmentVariable(key, value);

        // Builds the host (runs the API's startup seeding) and proves which database it uses.
        using var scope = Services.CreateScope();
        var actual = scope.ServiceProvider.GetRequiredService<ChildGuardDbContext>().Database.GetDbConnection().Database;
        if (actual != DatabaseName)
            throw new InvalidOperationException($"Integration API is connected to '{actual}', expected '{DatabaseName}'. Aborting.");
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        GuardIsolation();
        await using var server = new MySqlConnection(_serverConnectionString);
        await server.OpenAsync();
        await using var drop = server.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS `{DatabaseName}`";
        await drop.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<IFaceServiceClient>();
            services.AddSingleton<IFaceServiceClient>(Face);
        });
    }

    private void GuardIsolation()
    {
        if (!DatabaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to touch a database outside the integration-test prefix.");

        var host = new MySqlConnectionStringBuilder(_serverConnectionString).Server;
        var loopback = host is "localhost" or "127.0.0.1" or "::1";
        if (!loopback && Environment.GetEnvironmentVariable("CHILDGUARD_IT_ALLOW_REMOTE") != "1")
            throw new InvalidOperationException(
                $"Integration tests only run against a local database server (got '{host}'). " +
                "Set CHILDGUARD_IT_ALLOW_REMOTE=1 for a dedicated remote test server.");
    }

    // ------------------------------------------------------------------ helpers

    public ChildGuardDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ChildGuardDbContext>()
            .UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString)).Options);

    public async Task<HttpClient> AdminClientAsync()
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/admin/login", new LoginRequest(AdminEmail, AdminPassword));
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(Json.Options);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    /// <summary>A real token for a guardian, minted by the API's own generator.</summary>
    public HttpClient GuardianClient(int guardianId, string email = "guardian@childguard.test")
    {
        using var scope = Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var (token, _) = jwt.GenerateToken(guardianId, email, "Test Guardian", ChildGuard.Domain.Enums.AppRole.Guardian);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public SchoolClock Clock => Services.GetRequiredService<SchoolClock>();
}

/// <summary>Settable clock injected as the API's TimeProvider.</summary>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTime _utcNow;
    public TestTimeProvider(DateTime utcNow) => _utcNow = utcNow;

    public DateTime UtcNow
    {
        get => _utcNow;
        set => _utcNow = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public override DateTimeOffset GetUtcNow() => new(_utcNow, TimeSpan.Zero);
    public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);
}

/// <summary>Stands in for the Python face service: deterministic match / no-match.</summary>
public sealed class FakeFaceServiceClient : IFaceServiceClient
{
    public bool NextMatch { get; set; } = true;

    public Task<Result<FaceEmbeddingResult>> RegisterFaceAsync(string imageBase64, CancellationToken ct = default) =>
        Task.FromResult(Result<FaceEmbeddingResult>.Success(new FaceEmbeddingResult("[0.1,0.2,0.3]", "test-model")));

    public Task<Result<FaceMatchResult>> VerifyFaceAsync(string storedEmbeddingJson, string imageBase64, CancellationToken ct = default) =>
        Task.FromResult(Result<FaceMatchResult>.Success(new FaceMatchResult(NextMatch, NextMatch ? 0.93 : 0.21)));
}

public static class Json
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
