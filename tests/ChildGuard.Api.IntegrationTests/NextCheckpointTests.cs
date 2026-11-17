using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Pickup desk, automatic mode: the API picks the next checkpoint from today's school schedule and the student's
/// journey so far, and records nothing. The test clock puts "now" at 10:00 at the school on Fri 6 Nov 2026.
/// </summary>
public class NextCheckpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public NextCheckpointTests(ApiFactory api) => _api = api;

    private static async Task<NextCheckpointDto> NextAsync(HttpClient admin, Child child)
    {
        var r = await admin.PostAsJsonAsync("/api/rfid/next-checkpoint", new RfidIdentifyRequest(child.CardUid, child.DeviceId), Json.Options);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<NextCheckpointDto>(Json.Options))!;
    }

    [Fact]
    public async Task FollowsTheJourney_AndRecordsNothing()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();

        var first = await NextAsync(admin, child);
        Assert.True(first.StudentIdentified);
        Assert.Equal(JourneyEventType.SchoolArrival, first.Checkpoint); // no bus, 10:00 on a school day
        Assert.Equal("School arrival", first.CheckpointLabel);
        Assert.False(first.OffSchedule);
        Assert.Equal(JourneyEventType.SchoolArrival, (await NextAsync(admin, child)).Checkpoint); // asking twice records nothing

        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var afterArrival = await NextAsync(admin, child);
        Assert.Equal(JourneyEventType.SchoolDeparture, afterArrival.Checkpoint);
        Assert.Contains("early pickup", afterArrival.Reason); // 10:00 is before the 14:30 dismissal
    }

    [Fact]
    public async Task UnknownCard_IsNotIdentified()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var r = await NextAsync(admin, child with { CardUid = "NO-SUCH-CARD-" + Guid.NewGuid().ToString("N")[..6] });
        Assert.False(r.StudentIdentified);
        Assert.Null(r.Checkpoint);
    }

    [Fact]
    public async Task Guardian_IsForbidden()
    {
        var child = await _api.NewChildAsync();
        var r = await _api.GuardianClient(child.GuardianId).PostAsJsonAsync("/api/rfid/next-checkpoint",
            new RfidIdentifyRequest(child.CardUid, child.DeviceId), Json.Options);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
