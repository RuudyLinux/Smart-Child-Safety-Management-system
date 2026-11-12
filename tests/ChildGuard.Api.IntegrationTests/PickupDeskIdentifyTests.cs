using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// BUG-001: at a pickup checkpoint the desk must only identify the student. The
/// handover approval is the single writer of the SchoolDeparture event; a scan that
/// recorded it first made every approval fail ("already recorded").
/// </summary>
public class PickupDeskIdentifyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public PickupDeskIdentifyTests(ApiFactory api) => _api = api;

    private static Task<HttpResponseMessage> IdentifyAsync(HttpClient client, string cardUid, int deviceId) =>
        client.PostAsJsonAsync("/api/rfid/identify", new RfidIdentifyRequest(cardUid, deviceId), Json.Options);

    [Fact]
    public async Task Identify_ReturnsTheStudent_AndWritesNoJourneyEventOrAttendance()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);

        var response = await IdentifyAsync(admin, child.CardUid, child.DeviceId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<RfidIdentifyResultDto>(Json.Options))!;
        Assert.True(body.StudentIdentified);
        Assert.Equal(child.StudentId, body.StudentId);
        Assert.False(string.IsNullOrEmpty(body.StudentName));
        Assert.DoesNotContain(child.CardUid, body.MaskedCardUid);
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId));
        Assert.Equal(1, await db.AttendanceRecords.CountAsync(a => a.StudentId == child.StudentId));
    }

    [Fact]
    public async Task DeskPickupFlow_IdentifyThenVerifyThenApprove_RecordsExactlyOneDeparture()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);

        (await IdentifyAsync(admin, child.CardUid, child.DeviceId)).EnsureSuccessStatusCode();
        var verificationId = await admin.VerifyFaceAsync(_api, child);
        var approve = await admin.ApproveAsync(verificationId);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.JourneyEvents.CountAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolDeparture));
    }

    [Fact]
    public async Task Identify_UnknownCard_IsNotIdentified()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();

        var response = await IdentifyAsync(admin, "NOPE" + Guid.NewGuid().ToString("N")[..10], child.DeviceId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<RfidIdentifyResultDto>(Json.Options))!;
        Assert.False(body.StudentIdentified);
        Assert.Null(body.StudentId);
    }

    [Fact]
    public async Task Identify_IsAdminOnly()
    {
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);

        var response = await IdentifyAsync(guardian, child.CardUid, child.DeviceId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
