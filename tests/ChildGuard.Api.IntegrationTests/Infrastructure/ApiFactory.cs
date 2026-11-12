using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using Npgsql;

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
/// Engine: CHILDGUARD_IT_PROVIDER = mysql (default) or postgres. The same tests run on both.
/// Server credentials (no database name): CHILDGUARD_IT_MYSQL, default
/// "Server=127.0.0.1;Port=3306;User=root;Password=;" (XAMPP default), or CHILDGUARD_IT_POSTGRES,
/// default "Host=127.0.0.1;Port=5432;Username=postgres;SSL Mode=Disable".
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DatabasePrefix = "childguard_it_";
    public const string AdminEmail = "it-admin@childguard.test";

    private readonly string _serverConnectionString;

    public ApiFactory()
    {
        Provider = DatabaseProviders.Parse(Environment.GetEnvironmentVariable("CHILDGUARD_IT_PROVIDER"));
        DatabaseName = DatabasePrefix + Guid.NewGuid().ToString("N")[..12];
        if (Provider == DatabaseProvider.Postgres)
        {
            _serverConnectionString = Environment.GetEnvironmentVariable("CHILDGUARD_IT_POSTGRES")
                ?? "Host=127.0.0.1;Port=5432;Username=postgres;SSL Mode=Disable";
            ConnectionString = new NpgsqlConnectionStringBuilder(_serverConnectionString) { Database = DatabaseName }.ConnectionString;
        }
        else
        {
            _serverConnectionString = Environment.GetEnvironmentVariable("CHILDGUARD_IT_MYSQL")
                ?? "Server=127.0.0.1;Port=3306;User=root;Password=;";
            ConnectionString = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = DatabaseName }.ConnectionString;
        }
        AdminPassword = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)) + "!a";
        JwtSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
    }

    /// <summary>The engine this run targets (CHILDGUARD_IT_PROVIDER).</summary>
    public DatabaseProvider Provider { get; }

    public string DatabaseName { get; }
    public string ConnectionString { get; }
    public string AdminPassword { get; }
    public string JwtSecret { get; }

    /// <summary>The API's clock. Defaults to 10:00 at the school (Asia/Kolkata) on 6 Nov 2026.</summary>
    public TestTimeProvider Time { get; } = new(new DateTime(2026, 11, 6, 4, 30, 0, DateTimeKind.Utc));

    public FakeFaceServiceClient Face { get; } = new();

    /// <summary>Captures every login code the API "sends", so a test can complete the OTP step like a guardian would.</summary>
    public CapturingOtpSender Otp { get; } = new();

    /// <summary>Configuration overrides; subclasses can tighten e.g. rate limits.</summary>
    protected virtual IDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Database__Provider"] = Provider.ToString(),
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
        ["RateLimiting__FaceAnalyzePerMinute"] = "100000",
        ["RateLimiting__LiveVerifyPerMinute"] = "100000",
        ["Verification__RequireLiveness"] = "false",
        // Process-wide env vars outlive a factory, so unencrypted is stated explicitly each time.
        ["Biometrics__EncryptionKey"] = "",
    };

    public async Task InitializeAsync()
    {
        GuardIsolation();

        await CreateDatabaseAsync();
        await using (var db = NewDb())
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
        await DropDatabaseAsync();
    }

    private async Task CreateDatabaseAsync()
    {
        if (Provider == DatabaseProvider.Postgres)
        {
            await using var server = new NpgsqlConnection(_serverConnectionString);
            await server.OpenAsync();
            await using var create = server.CreateCommand();
            create.CommandText = $"CREATE DATABASE {Q(DatabaseName)} ENCODING 'UTF8'";
            await create.ExecuteNonQueryAsync();
        }
        else
        {
            await using var server = new MySqlConnection(_serverConnectionString);
            await server.OpenAsync();
            await using var create = server.CreateCommand();
            create.CommandText = $"CREATE DATABASE `{DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
            await create.ExecuteNonQueryAsync();
        }
    }

    private async Task DropDatabaseAsync()
    {
        if (Provider == DatabaseProvider.Postgres)
        {
            // Pooled client connections would block the drop.
            NpgsqlConnection.ClearAllPools();
            await using var server = new NpgsqlConnection(_serverConnectionString);
            await server.OpenAsync();
            await using var drop = server.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {Q(DatabaseName)} WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }
        else
        {
            await using var server = new MySqlConnection(_serverConnectionString);
            await server.OpenAsync();
            await using var drop = server.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS `{DatabaseName}`";
            await drop.ExecuteNonQueryAsync();
        }
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
            services.RemoveAll<IOtpSender>();
            services.AddSingleton<IOtpSender>(Otp);
        });
    }

    private void GuardIsolation()
    {
        if (!DatabaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to touch a database outside the integration-test prefix.");

        var host = Provider == DatabaseProvider.Postgres
            ? new NpgsqlConnectionStringBuilder(_serverConnectionString).Host
            : new MySqlConnectionStringBuilder(_serverConnectionString).Server;
        var loopback = host is "localhost" or "127.0.0.1" or "::1";
        if (!loopback && Environment.GetEnvironmentVariable("CHILDGUARD_IT_ALLOW_REMOTE") != "1")
            throw new InvalidOperationException(
                $"Integration tests only run against a local database server (got '{host}'). " +
                "Set CHILDGUARD_IT_ALLOW_REMOTE=1 for a dedicated remote test server.");
    }

    // ------------------------------------------------------------------ helpers

    public ChildGuardDbContext NewDb() =>
        new(Provider == DatabaseProvider.Postgres
            ? new DbContextOptionsBuilder<ChildGuardDbContext>()
                .UseNpgsql(ConnectionString, o => o.MigrationsAssembly("ChildGuard.Infrastructure.Postgres")).Options
            : new DbContextOptionsBuilder<ChildGuardDbContext>()
                .UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString)).Options);

    /// <summary>Quotes an identifier for raw SQL: PostgreSQL needs double quotes to keep mixed case, MySQL needs none.</summary>
    public string Q(string identifier) => Provider == DatabaseProvider.Postgres ? $"\"{identifier}\"" : identifier;

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
public sealed class CapturingOtpSender : IOtpSender
{
    public List<(int GuardianId, string Code)> Sent { get; } = new();
    public string? FailWith { get; set; }
    public string LastCode => Sent[^1].Code;

    public Task<Result> SendLoginOtpAsync(Guardian guardian, string code, TimeSpan validFor, CancellationToken ct = default)
    {
        if (FailWith is not null) return Task.FromResult(Result.Failure(FailWith));
        lock (Sent) Sent.Add((guardian.GuardianId, code));
        return Task.FromResult(Result.Success());
    }
}

public sealed class FakeFaceServiceClient : IFaceServiceClient
{
    public bool NextMatch { get; set; } = true;

    public Task<Result<FaceEmbeddingResult>> RegisterFaceAsync(string imageBase64, CancellationToken ct = default) =>
        Task.FromResult(Result<FaceEmbeddingResult>.Success(new FaceEmbeddingResult("[0.1,0.2,0.3]", "test-model")));

    /// <summary>The stored embedding the API last handed over for a single-face comparison (what the service would see).</summary>
    public string? LastStoredEmbedding { get; private set; }

    public Task<Result<FaceMatchResult>> VerifyFaceAsync(string storedEmbeddingJson, string imageBase64, CancellationToken ct = default)
    {
        LastStoredEmbedding = storedEmbeddingJson;
        return Task.FromResult(Result<FaceMatchResult>.Success(new FaceMatchResult(NextMatch, NextMatch ? 0.93 : 0.21)));
    }

    public Result<ImageAnalysis> NextAnalysis { get; set; } = Result<ImageAnalysis>.Success(Clean);

    public static ImageAnalysis Clean { get; } = new(
        "verification", 1, [], [], [], new ImageAnalysisMetrics(300, 150, 40, 200, 0, 0.99));

    public Task<Result<ImageAnalysis>> AnalyzeImageAsync(string imageBase64, string purpose, CancellationToken ct = default) =>
        Task.FromResult(NextAnalysis);


    /// <summary>A liveness analysis that passes every signal for the first candidate; tests tweak it with <c>with</c>.</summary>
    public static LivenessAnalysis PassingLiveness(IReadOnlyList<string> steps, IReadOnlyList<IdentifyCandidate>? candidates, double score = 0.93, int? guardianId = null)
    {
        var identification = candidates is null ? null : new FaceIdentification(0.40, "test-model",
            candidates.Select(c => new GuardianIdentification(c.GuardianId,
                c.GuardianId == (guardianId ?? candidates[0].GuardianId) ? score : 0.05,
                c.References.Count > 0 ? c.References[0].ReferenceId : null,
                c.References.Select(r => new ReferenceScore(r.ReferenceId, score)).ToList(), [])).ToList());
        return new LivenessAnalysis(
            10, 1.0, 2,
            steps.Select((s, i) => new LivenessStepResult(s, true, 4 + i * 3, s == "turn_left" ? 0.35 : -0.35)).ToList(),
            true, new PassiveSignals(true, 5, [0.97, 0.98, 0.96, 0.97, 0.98], 1.0, 0.972, 0.96),
            new ContinuitySignals(2, 0.7, true), 0.0, [], identification,
            new ImageAnalysis("verification", 1, [], [], [], new ImageAnalysisMetrics(300, 150, 40, 200, 0, 0.99)));
    }

    public Func<IReadOnlyList<string>, IReadOnlyList<IdentifyCandidate>?, LivenessAnalysis>? LivenessFactory { get; set; }
    public Result<LivenessAnalysis>? NextLivenessFailure { get; set; }
    public List<IReadOnlyList<LiveFrameInput>> LivenessFrames { get; } = new();
    public List<IReadOnlyList<string>> LivenessSteps { get; } = new();
    public Dictionary<int, double> ScoreByGuardian { get; } = new();

    public Task<Result<LivenessAnalysis>> AnalyzeLivenessAsync(
        IReadOnlyList<string> steps, IReadOnlyList<LiveFrameInput> frames, IReadOnlyList<IdentifyCandidate>? candidates, CancellationToken ct = default)
    {
        LivenessFrames.Add(frames);
        LivenessSteps.Add(steps);
        if (NextLivenessFailure is not null) return Task.FromResult(NextLivenessFailure);
        return Task.FromResult(Result<LivenessAnalysis>.Success(
            LivenessFactory?.Invoke(steps, candidates) ?? PassingLiveness(steps, candidates, NextIdentifyScore)));
    }

    public double NextIdentifyScore { get; set; } = 0.93;
    public int? NextDuplicateOf { get; set; }
    public double? NextBestExistingScore { get; set; } = 0.5;
    public List<IReadOnlyList<IdentifyCandidate>> IdentifyCalls { get; } = new();

    public Task<Result<FaceIdentification>> IdentifyAsync(string imageBase64, IReadOnlyList<IdentifyCandidate> candidates, CancellationToken ct = default)
    {
        IdentifyCalls.Add(candidates);
        var results = candidates.Select(c => new GuardianIdentification(
            c.GuardianId, ScoreByGuardian.GetValueOrDefault(c.GuardianId, NextIdentifyScore), c.References.Count > 0 ? c.References[0].ReferenceId : null,
            c.References.Select(r => new ReferenceScore(r.ReferenceId, ScoreByGuardian.GetValueOrDefault(c.GuardianId, NextIdentifyScore))).ToList(), [])).ToList();
        return Task.FromResult(Result<FaceIdentification>.Success(new FaceIdentification(0.40, "test-model", results)));
    }

    public Task<Result<ReferenceRegistration>> RegisterReferenceAsync(string imageBase64, IReadOnlyList<ReferenceEmbedding> existing, CancellationToken ct = default) =>
        Task.FromResult(Result<ReferenceRegistration>.Success(new ReferenceRegistration("[0.5,0.5,0.5]", "test-model", NextDuplicateOf, NextBestExistingScore, 0.40)));
}

public static class Json
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
