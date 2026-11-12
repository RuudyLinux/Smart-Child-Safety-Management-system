using System.Net;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>Schema/migration state and historical-record protection.</summary>
public class DatabaseTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public DatabaseTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task AllMigrationsAreApplied_AndModelHasNoPendingChanges()
    {
        await using var db = _api.NewDb();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("FK_AttendanceRecords_Students_StudentId")]
    [InlineData("FK_JourneyEvents_Students_StudentId")]
    [InlineData("FK_GuardianVerifications_Students_StudentId")]
    [InlineData("FK_GuardianVerifications_Guardians_GuardianId")]
    [InlineData("FK_StudentBusAssignments_Buses_BusId")]
    public async Task HistoricalForeignKeys_AreRestrict(string constraint)
    {
        await using var db = _api.NewDb();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // pg_constraint.confdeltype 'r' = RESTRICT; MySQL reports the rule by name.
        cmd.CommandText = _api.Provider == DatabaseProvider.Postgres
            ? "SELECT CASE confdeltype WHEN 'r' THEN 'RESTRICT' ELSE confdeltype::text END FROM pg_constraint WHERE conname = @name"
            : "SELECT DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = @name";
        var p = cmd.CreateParameter(); p.ParameterName = "@name"; p.Value = constraint; cmd.Parameters.Add(p);

        Assert.Equal("RESTRICT", (string?)await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task HardDeletingAStudentWithHistory_IsBlocked_AndHistorySurvives()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);

        await using (var db = _api.NewDb())
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlRawAsync($"DELETE FROM {_api.Q("Students")} WHERE {_api.Q("StudentId")} = {{0}}", child.StudentId));
        }

        await using var check = _api.NewDb();
        Assert.True(await check.Students.AnyAsync(s => s.StudentId == child.StudentId));
        Assert.Equal(1, await check.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId));
        Assert.Equal(1, await check.AttendanceRecords.CountAsync(a => a.StudentId == child.StudentId));
    }

    [Fact]
    public async Task HardDeletingAGuardianWithVerificationHistory_IsBlocked()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.VerifyFaceAsync(_api, child, match: false);

        await using var db = _api.NewDb();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.ExecuteSqlRawAsync($"DELETE FROM {_api.Q("Guardians")} WHERE {_api.Q("GuardianId")} = {{0}}", child.GuardianId));
        Assert.Equal(1, await db.GuardianVerifications.CountAsync(v => v.GuardianId == child.GuardianId));
    }

    [Fact]
    public async Task LegacyUtcAttendanceRows_AreProjectedToSchoolTimeExactlyOnce()
    {
        var child = await _api.NewChildAsync();
        var instant = new DateTime(2026, 10, 20, 2, 25, 0); // 07:55 at the school
        int id;
        await using (var db = _api.NewDb())
        {
            // A row as the pre-time-zone code wrote it: Date/Time = the UTC instant.
            var row = new Attendance
            {
                StudentId = child.StudentId, EventType = AttendanceEventType.SchoolArrival,
                RecordedAtUtc = instant, Date = DateOnly.FromDateTime(instant), Time = TimeOnly.FromDateTime(instant)
            };
            db.AttendanceRecords.Add(row);
            await db.SaveChangesAsync();
            id = row.AttendanceId;
        }

        var first = await AttendanceTimeProjection.SyncAsync(_api.Services);
        var second = await AttendanceTimeProjection.SyncAsync(_api.Services);

        await using var check = _api.NewDb();
        var converted = await check.AttendanceRecords.SingleAsync(a => a.AttendanceId == id);
        Assert.True(first >= 1);
        Assert.Equal(0, second);
        Assert.Equal(new DateOnly(2026, 10, 20), converted.Date);
        Assert.Equal(new TimeOnly(7, 55), converted.Time);
        Assert.Equal(instant, converted.RecordedAtUtc); // original preserved
    }
}

/// <summary>A factory with a deliberately small scan limit, to prove the limiter partitions per scanner.</summary>
public sealed class TightScanLimitFactory : ApiFactory
{
    protected override IDictionary<string, string> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["RateLimiting__RfidScanPerMinute"] = "5";
            return settings;
        }
    }
}

public class RateLimitTests : IClassFixture<TightScanLimitFactory>
{
    private readonly TightScanLimitFactory _api;
    public RateLimitTests(TightScanLimitFactory api) => _api = api;

    [Fact]
    public async Task ScanLimit_IsPerScanner_NotShared()
    {
        var child = await _api.NewChildAsync();
        int secondDevice;
        await using (var db = _api.NewDb())
        {
            var d = new RfidDevice { DeviceLabel = "IT-LIMIT-2" };
            db.RfidDevices.Add(d);
            await db.SaveChangesAsync();
            secondDevice = d.RfidDeviceId;
        }
        var admin = await _api.AdminClientAsync();
        var keyA = await IssueKeyAsync(admin, child.DeviceId);
        var keyB = await IssueKeyAsync(admin, secondDevice);
        var scannerA = Scanner(keyA);
        var scannerB = Scanner(keyB);

        var codesA = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++) codesA.Add((await scannerA.ScanAsync(child, JourneyEventType.SchoolArrival)).StatusCode);
        var b = await scannerB.ScanAsync(child with { DeviceId = secondDevice }, JourneyEventType.SchoolArrival);

        Assert.Equal(5, codesA.Count(c => c == HttpStatusCode.OK));
        Assert.Contains(HttpStatusCode.TooManyRequests, codesA);
        Assert.Equal(HttpStatusCode.OK, b.StatusCode); // scanner B has its own bucket
    }

    private HttpClient Scanner(string key)
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Key", key);
        return client;
    }

    private static async Task<string> IssueKeyAsync(HttpClient admin, int deviceId)
    {
        var response = await admin.PostAsync($"/api/rfid/devices/{deviceId}/credentials", null);
        response.EnsureSuccessStatusCode();
        var body = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<ChildGuard.Application.DTOs.DeviceCredentialDto>(response.Content, Json.Options);
        return body!.ApiKey;
    }
}
