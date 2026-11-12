using System.Text.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Instants are stored in UTC. Once read back from MySQL they must still leave the
/// API marked as UTC ("...Z"); without the marker every client (browser "x min ago"
/// labels, the Flutter app's .toLocal()) reads them as local time and is off by the
/// zone offset — 5h30 in Asia/Kolkata.
/// </summary>
public class TimestampSerializationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public TimestampSerializationTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task JourneyTimestamps_ReadFromDatabase_AreSerializedAsUtc()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);

        var json = await admin.GetStringAsync($"/api/journey/{child.StudentId}");
        using var doc = JsonDocument.Parse(json);
        var events = doc.RootElement.EnumerateArray().ToList();

        Assert.NotEmpty(events);
        foreach (var e in events)
        {
            var raw = e.GetProperty("timestamp").GetString()!;
            Assert.True(raw.EndsWith('Z'), $"timestamp '{raw}' has no UTC designator");
            Assert.Equal(_api.Time.UtcNow, DateTime.Parse(raw).ToUniversalTime(), TimeSpan.FromSeconds(1));
        }
    }
}
