using System.Net.Http.Json;
using System.Text.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// MySQL compares and sorts text case-insensitively; PostgreSQL does not. These tests pin the
/// behaviour the app relies on (logins, uniqueness, list order, search) so both engines give the same answer.
/// They run on whichever engine CHILDGUARD_IT_PROVIDER selects.
/// </summary>
public class DatabaseProviderParityTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public DatabaseProviderParityTests(ApiFactory api) => _api = api;

    private static string Token() => Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task TheSuiteRunsOnTheEngineThatWasRequested()
    {
        await using var db = _api.NewDb();
        var expected = _api.Provider == DatabaseProvider.Postgres
            ? "Npgsql.EntityFrameworkCore.PostgreSQL"
            : "Pomelo.EntityFrameworkCore.MySql";
        Assert.Equal(expected, db.Database.ProviderName);

        // The API under test must be on the same engine, not just the helper context.
        using var scope = _api.Services.CreateScope();
        var apiDb = scope.ServiceProvider.GetRequiredService<ChildGuardDbContext>();
        Assert.Equal(expected, apiDb.Database.ProviderName);
    }

    [Fact]
    public async Task AdminEmail_LookupIgnoresCase()
    {
        await using var db = _api.NewDb();
        Assert.True(await db.Admins.AnyAsync(a => a.Email == ApiFactory.AdminEmail.ToUpperInvariant()));
    }

    [Fact]
    public async Task AdminLogin_WorksWithDifferentlyCasedEmail()
    {
        var client = _api.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/admin/login",
            new LoginRequest(ApiFactory.AdminEmail.ToUpperInvariant(), _api.AdminPassword));
        Assert.True(login.IsSuccessStatusCode, $"login returned {(int)login.StatusCode}");
    }

    [Fact]
    public async Task GuardianEmail_IsUniqueIgnoringCase_AndLookupIgnoresCase()
    {
        var token = Token();
        await using var db = _api.NewDb();
        db.Guardians.Add(new Guardian { FullName = "Case One", Email = $"Parent.{token}@Example.com", Phone = $"9{Random.Shared.NextInt64(100000000, 999999999)}", PasswordHash = "x" });
        await db.SaveChangesAsync();

        Assert.True(await db.Guardians.AnyAsync(g => g.Email == $"parent.{token}@example.com"));

        await using var second = _api.NewDb();
        second.Guardians.Add(new Guardian { FullName = "Case Two", Email = $"PARENT.{token}@EXAMPLE.COM", Phone = $"9{Random.Shared.NextInt64(100000000, 999999999)}", PasswordHash = "x" });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.True(DbErrors.IsUniqueViolation(ex));
    }

    [Fact]
    public async Task BusNumber_IsUniqueIgnoringCase()
    {
        var token = Token();
        await using var db = _api.NewDb();
        db.Buses.Add(new Bus { BusNumber = $"Bus-{token}", Capacity = 30 });
        await db.SaveChangesAsync();

        await using var second = _api.NewDb();
        second.Buses.Add(new Bus { BusNumber = $"BUS-{token.ToUpperInvariant()}", Capacity = 30 });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.True(DbErrors.IsUniqueViolation(ex));
    }

    [Fact]
    public async Task CardUid_IsUniqueIgnoringCase_AndLookupIgnoresCase()
    {
        var child = await _api.NewChildAsync();
        await using var db = _api.NewDb();
        Assert.NotNull(await db.RfidCards.FirstOrDefaultAsync(c => c.CardUid == child.CardUid.ToLowerInvariant()));

        db.RfidCards.Add(new RfidCard { CardUid = child.CardUid.ToLowerInvariant(), AssignedAt = DateTime.UtcNow });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(DbErrors.IsUniqueViolation(ex));
    }

    [Fact]
    public async Task StudentList_IsOrderedByNameIgnoringCase()
    {
        var token = Token();
        await using (var db = _api.NewDb())
        {
            foreach (var name in new[] { $"zed {token}", $"Bob {token}", $"alice {token}", $"Carl {token}" })
                db.Students.Add(new Student { FullName = name, DateOfBirth = new DateTime(2016, 5, 5), Grade = "1" });
            await db.SaveChangesAsync();
        }

        await using var read = _api.NewDb();
        var ordered = await read.Students.Where(s => s.FullName.EndsWith(token)).OrderBy(s => s.FullName).Select(s => s.FullName).ToListAsync();
        Assert.Equal(new[] { $"alice {token}", $"Bob {token}", $"Carl {token}", $"zed {token}" }, ordered);
    }

    [Fact]
    public async Task StudentSearch_IgnoresCase_AndTreatsWildcardsAsLiteralText()
    {
        var admin = await _api.AdminClientAsync();
        var token = Token();
        foreach (var name in new[] { $"Zebra {token} A_B", $"Zebra {token} AXB", $"Zebra {token} 50% done" })
        {
            var created = await admin.PostAsJsonAsync("/api/students", new CreateStudentRequest(name, new DateTime(2016, 2, 2), "2"), Json.Options);
            created.EnsureSuccessStatusCode();
        }

        async Task<List<string>> Search(string term)
        {
            var response = await admin.GetAsync($"/api/students?search={Uri.EscapeDataString(term)}");
            response.EnsureSuccessStatusCode();
            var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
            return rows.EnumerateArray().Select(r => r.GetProperty("fullName").GetString()!).ToList();
        }

        Assert.Equal(3, (await Search($"zEBRA {token}")).Count);          // case-insensitive
        Assert.Equal(new[] { $"Zebra {token} A_B" }, await Search($"{token} a_b"));  // "_" is not "any one character"
        Assert.Equal(new[] { $"Zebra {token} 50% done" }, await Search($"50% DONE")); // "%" is not "anything"
    }

    [Fact]
    public async Task StudentDateOfBirth_RoundTripsUnchanged()
    {
        var admin = await _api.AdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/students",
            new CreateStudentRequest($"Birthday {Token()}", new DateTime(2015, 3, 9), "4"), Json.Options);
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("studentId").GetInt32();

        var read = await admin.GetFromJsonAsync<JsonElement>($"/api/students/{id}");
        Assert.StartsWith("2015-03-09", read.GetProperty("dateOfBirth").GetString());
    }

    [Fact]
    public async Task Instants_RoundTripAsUtc_EvenWhenWrittenWithUnspecifiedKind()
    {
        var child = await _api.NewChildAsync();
        var unspecified = new DateTime(2026, 11, 6, 4, 30, 15, 123, DateTimeKind.Unspecified);
        int id;
        await using (var db = _api.NewDb())
        {
            var notification = new Notification { GuardianId = child.GuardianId, StudentId = child.StudentId, Title = "t", Message = "m", SentAt = unspecified };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            id = notification.NotificationId;
        }

        await using var read = _api.NewDb();
        var stored = (await read.Notifications.FirstAsync(n => n.NotificationId == id)).SentAt;
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(unspecified.Ticks, stored.Ticks);
    }
}
