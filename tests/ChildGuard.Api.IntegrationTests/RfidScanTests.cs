using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>RFID scanning against the real database: duplicates, idempotency, scanner credentials.</summary>
public class RfidScanTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public RfidScanTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task ConcurrentIdenticalTaps_RecordExactlyOneEventAndOneAttendance()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();

        var results = await TestData.Simultaneously(12, _ => admin.ScanOkAsync(child, JourneyEventType.SchoolArrival));

        Assert.Single(results, r => r.EventRecorded && !r.AlreadyRecorded);
        Assert.Equal(11, results.Count(r => r.AlreadyRecorded));
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId));
        Assert.Equal(1, await db.AttendanceRecords.CountAsync(a => a.StudentId == child.StudentId));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.StudentId == child.StudentId));
    }

    [Fact]
    public async Task RepeatWithinWindow_IsAlreadyRecorded_AfterWindow_IsRejected()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.BusBoarding);

        _api.Time.Advance(TimeSpan.FromSeconds(120));
        var within = await admin.ScanOkAsync(child, JourneyEventType.BusBoarding);
        _api.Time.Advance(TimeSpan.FromSeconds(1));
        var after = await admin.ScanOkAsync(child, JourneyEventType.BusBoarding);

        Assert.True(within.AlreadyRecorded);
        Assert.Equal("Already recorded.", within.Message);
        Assert.False(after.EventRecorded);
        Assert.False(after.AlreadyRecorded);
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId));
    }

    [Fact]
    public async Task RetriedScanId_IsIdempotentEvenWhenConcurrent()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();

        var results = await TestData.Simultaneously(6, _ => admin.ScanOkAsync(child, JourneyEventType.SchoolArrival, scanId: "reader-7:000123"));
        _api.Time.Advance(TimeSpan.FromMinutes(30)); // long after the window
        var late = await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival, scanId: "reader-7:000123");

        Assert.Single(results, r => !r.AlreadyRecorded);
        Assert.True(late.AlreadyRecorded);
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId));
    }

    [Fact]
    public async Task FullJourneyCycle_ThenNewCycle_IsAccepted()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        foreach (var checkpoint in new[] { JourneyEventType.BusBoarding, JourneyEventType.SchoolArrival, JourneyEventType.SchoolDeparture, JourneyEventType.BusDropoff, JourneyEventType.BusBoarding })
        {
            var r = await admin.ScanOkAsync(child, checkpoint);
            Assert.True(r.EventRecorded && !r.AlreadyRecorded, $"{checkpoint}: {r.Message}");
            _api.Time.Advance(TimeSpan.FromMinutes(5));
        }
    }

    [Fact]
    public async Task OutOfOrderCheckpoint_IsRejectedWithReason()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();

        var r = await admin.ScanOkAsync(child, JourneyEventType.BusDropoff);

        Assert.False(r.EventRecorded);
        Assert.Contains("not valid", r.Message);
    }

    [Theory]
    [InlineData("""{"rfidDeviceId":1,"checkpoint":"BusBoarding"}""")]                    // no card
    [InlineData("""{"cardUid":"X","rfidDeviceId":0,"checkpoint":"BusBoarding"}""")]       // bad device
    [InlineData("""{"cardUid":"X","rfidDeviceId":1,"checkpoint":"Teleport"}""")]          // bad enum
    [InlineData("""{"cardUid":"X","rfidDeviceId":1,"checkpoint":"BusBoarding","scanId":"has spaces!"}""")]
    [InlineData("""{"cardUid":""")]                                                        // malformed JSON
    public async Task InvalidScanRequests_Return400(string json)
    {
        var admin = await _api.AdminClientAsync();

        var response = await admin.PostAsync("/api/rfid/scan", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ScannerKey_AuthenticatesOnlyAsItsOwnDevice_AndCanBeRevoked()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        int otherDevice;
        await using (var db = _api.NewDb())
        {
            var d = new ChildGuard.Domain.Entities.RfidDevice { DeviceLabel = "IT-" + Guid.NewGuid().ToString("N")[..8] };
            db.RfidDevices.Add(d);
            await db.SaveChangesAsync();
            otherDevice = d.RfidDeviceId;
        }

        var issued = await admin.PostAsync($"/api/rfid/devices/{child.DeviceId}/credentials", null);
        issued.EnsureSuccessStatusCode();
        var credential = (await issued.Content.ReadFromJsonAsync<DeviceCredentialDto>(Json.Options))!;
        Assert.StartsWith("cgd_", credential.ApiKey);

        var scanner = _api.CreateClient();
        scanner.DefaultRequestHeaders.Add("X-Device-Key", credential.ApiKey);

        var own = await scanner.ScanAsync(child, JourneyEventType.SchoolArrival);
        var impersonating = await scanner.ScanAsync(child with { DeviceId = otherDevice }, JourneyEventType.SchoolDeparture);
        var adminEndpoint = await scanner.GetAsync("/api/students");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, impersonating.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, adminEndpoint.StatusCode);

        // Only the hash is stored.
        await using (var db = _api.NewDb())
        {
            var stored = await db.RfidDevices.SingleAsync(d => d.RfidDeviceId == child.DeviceId);
            Assert.NotEqual(credential.ApiKey, stored.ApiKeyHash);
            Assert.Equal(64, stored.ApiKeyHash!.Length);
        }

        (await admin.DeleteAsync($"/api/rfid/devices/{child.DeviceId}/credentials")).EnsureSuccessStatusCode();
        var afterRevoke = await scanner.ScanAsync(child, JourneyEventType.SchoolDeparture);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task UnknownScannerKey_IsUnauthorized()
    {
        var child = await _api.NewChildAsync();
        var scanner = _api.CreateClient();
        scanner.DefaultRequestHeaders.Add("X-Device-Key", "cgd_not-a-real-key");

        var response = await scanner.ScanAsync(child, JourneyEventType.SchoolArrival);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IssuingCredential_RequiresAdmin()
    {
        var child = await _api.NewChildAsync();

        var anonymous = await _api.CreateClient().PostAsync($"/api/rfid/devices/{child.DeviceId}/credentials", null);
        var guardian = await _api.GuardianClient(child.GuardianId).PostAsync($"/api/rfid/devices/{child.DeviceId}/credentials", null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, guardian.StatusCode);
    }
}
