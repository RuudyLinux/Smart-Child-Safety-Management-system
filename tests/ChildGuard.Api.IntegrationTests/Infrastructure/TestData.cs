using System.Net.Http.Json;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests.Infrastructure;

public sealed record Child(int StudentId, string CardUid, int GuardianId, int DeviceId);

/// <summary>Arranges data straight in the test database so each test starts from a known state.</summary>
public static class TestData
{
    private static int _seq;

    /// <summary>An active student with an active card and one pickup-authorised guardian with face data.</summary>
    public static async Task<Child> NewChildAsync(this ApiFactory api)
    {
        var n = Interlocked.Increment(ref _seq);
        await using var db = api.NewDb();
        var device = await db.RfidDevices.OrderBy(d => d.RfidDeviceId).FirstAsync();
        var student = new Student { FullName = $"Test Child {n}", DateOfBirth = new DateTime(2016, 1, 1), Grade = "3A" };
        var guardian = new Guardian
        {
            FullName = $"Test Guardian {n}", Email = $"guardian{n}.{Guid.NewGuid():N}@childguard.test",
            Phone = $"98{Random.Shared.Next(10000000, 99999999)}", PasswordHash = "not-used"
        };
        var card = new RfidCard { CardUid = $"IT{Guid.NewGuid():N}"[..16].ToUpperInvariant(), Student = student, AssignedAt = DateTime.UtcNow };
        db.AddRange(student, guardian, card,
            new StudentGuardian { Student = student, Guardian = guardian, IsAuthorizedForPickup = true, Relationship = "Parent" },
            new GuardianFaceData { Guardian = guardian, EmbeddingJson = "[0.1,0.2,0.3]", Model = "test-model", IsActive = true });
        await db.SaveChangesAsync();
        return new Child(student.StudentId, card.CardUid, guardian.GuardianId, device.RfidDeviceId);
    }

    public static Task<HttpResponseMessage> ScanAsync(this HttpClient client, Child child, JourneyEventType checkpoint, string? scanId = null) =>
        client.PostAsJsonAsync("/api/rfid/scan", new RfidScanRequest(child.CardUid, child.DeviceId, checkpoint, scanId), Json.Options);

    public static async Task<RfidScanResultDto> ScanOkAsync(this HttpClient client, Child child, JourneyEventType checkpoint, string? scanId = null)
    {
        var response = await client.ScanAsync(child, checkpoint, scanId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RfidScanResultDto>(Json.Options))!;
    }

    /// <summary>Runs the real face-verify endpoint (face service faked) and returns the verification id.</summary>
    public static async Task<int> VerifyFaceAsync(this HttpClient admin, ApiFactory api, Child child, bool match = true)
    {
        api.Face.NextMatch = match;
        var response = await admin.PostAsJsonAsync("/api/verification/face-verify",
            new FaceVerifyRequest(child.StudentId, child.GuardianId, "aW1hZ2U="), Json.Options);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<FaceVerifyResultDto>(Json.Options);
        return body!.VerificationId;
    }

    public static Task<HttpResponseMessage> ApproveAsync(this HttpClient admin, int verificationId) =>
        admin.PostAsJsonAsync("/api/verification/approve", new ApprovePickupRequest(verificationId), Json.Options);

    public static Task<HttpResponseMessage> RejectAsync(this HttpClient admin, int verificationId, string? reason = "test") =>
        admin.PostAsJsonAsync("/api/verification/reject", new RejectPickupRequest(verificationId, reason), Json.Options);

    public static async Task<string> MessageAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.TryGetProperty("message", out var m) ? m.GetString() ?? "" : body.ToString();
    }

    /// <summary>Fires all calls at once (released together) and returns them in order.</summary>
    public static async Task<T[]> Simultaneously<T>(int count, Func<int, Task<T>> call)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, count).Select(async i => { await gate.Task; return await call(i); }).ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }
}
