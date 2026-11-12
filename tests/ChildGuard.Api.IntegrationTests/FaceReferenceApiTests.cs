using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>Several reference faces per guardian: add, list, remove, and match against all of them.</summary>
public class FaceReferenceApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public FaceReferenceApiTests(ApiFactory api) => _api = api;

    private static string Url(int guardianId) => $"/api/guardians/{guardianId}/face-references";

    private async Task<HttpResponseMessage> AddAsync(HttpClient client, int guardianId, string? label = null)
    {
        _api.Face.NextDuplicateOf = null;
        return await client.PostAsJsonAsync(Url(guardianId), new AddFaceReferenceRequest("aW1hZ2U=", label), Json.Options);
    }

    [Fact]
    public async Task FirstPhoto_BecomesThePrimaryFace_SoTheGuardianIsFaceRegistered()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);

        var response = await AddAsync(admin, child.GuardianId);

        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<AddFaceReferenceResultDto>(Json.Options))!;
        Assert.True(body.Added);
        await using var db = _api.NewDb();
        Assert.Equal(1, await db.GuardianFaceData.CountAsync(f => f.GuardianId == child.GuardianId && f.IsActive));
        Assert.Equal(0, await db.GuardianFaceReferences.CountAsync(r => r.GuardianId == child.GuardianId));
    }

    [Fact]
    public async Task FurtherPhotos_AreStoredAsReferences_AndListedWithoutAnyEmbedding()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        await AddAsync(admin, child.GuardianId);
        var second = await AddAsync(admin, child.GuardianId, "Outdoor");
        second.EnsureSuccessStatusCode();

        var list = await admin.GetAsync(Url(child.GuardianId));

        list.EnsureSuccessStatusCode();
        var text = await list.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<GuardianFaceReferencesDto>(text, Json.Options)!;
        Assert.Equal(2, dto.References.Count);
        Assert.Equal("Outdoor", dto.References[1].Label);
        Assert.DoesNotContain("0.5,0.5", text);
        Assert.DoesNotContain("mbedding", text);
        Assert.DoesNotContain("enc:", text);
    }

    [Fact]
    public async Task TheLimitIsEnforced()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        for (var i = 0; i < 5; i++) (await AddAsync(admin, child.GuardianId)).EnsureSuccessStatusCode();

        var sixth = await AddAsync(admin, child.GuardianId);

        Assert.Equal(HttpStatusCode.BadRequest, sixth.StatusCode);
        Assert.Contains("at most 5", await sixth.MessageAsync());
    }

    [Fact]
    public async Task ADuplicatePhoto_IsAcceptedButNotStored()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        await AddAsync(admin, child.GuardianId);
        _api.Face.NextDuplicateOf = 0;

        var response = await admin.PostAsJsonAsync(Url(child.GuardianId), new AddFaceReferenceRequest("aW1hZ2U="), Json.Options);

        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<AddFaceReferenceResultDto>(Json.Options))!;
        Assert.False(body.Added);
        Assert.True(body.Duplicate);
        await using var db = _api.NewDb();
        Assert.Equal(0, await db.GuardianFaceReferences.CountAsync(r => r.GuardianId == child.GuardianId));
        _api.Face.NextDuplicateOf = null;
    }

    [Fact]
    public async Task ABadPhoto_IsRefusedWithTheReason()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        _api.Face.NextAnalysis = ChildGuard.Application.Common.Result<ChildGuard.Application.Interfaces.ImageAnalysis>.Success(
            new("registration", 2, ["MULTIPLE_FACES"], [], ["More than one face was detected."], new(300, 150, 40, 200, 0, 0.99)));

        var response = await AddAsync(admin, child.GuardianId);

        _api.Face.NextAnalysis = ChildGuard.Application.Common.Result<ChildGuard.Application.Interfaces.ImageAnalysis>.Success(FakeFaceServiceClient.Clean);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("More than one face", await response.MessageAsync());
        await using var db = _api.NewDb();
        Assert.Equal(0, await db.GuardianFaceData.CountAsync(f => f.GuardianId == child.GuardianId));
    }

    [Fact]
    public async Task Remove_DeactivatesTheReference_AndAnotherGuardiansIdIsRefused()
    {
        var admin = await _api.AdminClientAsync();
        var mine = await _api.NewChildAsync(withFace: false);
        var theirs = await _api.NewChildAsync(withFace: false);
        foreach (var c in new[] { mine, theirs })
        {
            await AddAsync(admin, c.GuardianId);
            await AddAsync(admin, c.GuardianId, "extra");
        }
        await using var db = _api.NewDb();
        var myRef = await db.GuardianFaceReferences.SingleAsync(r => r.GuardianId == mine.GuardianId);
        var theirRef = await db.GuardianFaceReferences.SingleAsync(r => r.GuardianId == theirs.GuardianId);

        var crossed = await admin.DeleteAsync($"{Url(mine.GuardianId)}/{theirRef.Id}");
        var own = await admin.DeleteAsync($"{Url(mine.GuardianId)}/{myRef.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        await using var after = _api.NewDb();
        Assert.False((await after.GuardianFaceReferences.FindAsync(myRef.Id))!.IsActive);
        Assert.True((await after.GuardianFaceReferences.FindAsync(theirRef.Id))!.IsActive);
    }

    [Fact]
    public async Task ThePrimaryFace_CannotBeRemovedThroughTheReferenceEndpoint()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();

        var response = await admin.DeleteAsync($"{Url(child.GuardianId)}/0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownGuardian_IsNotFound()
    {
        var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(Url(999999))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(admin, 999999)).StatusCode);
    }

    [Fact]
    public async Task WithSeveralReferences_VerificationComparesAgainstAllOfThem()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        await AddAsync(admin, child.GuardianId);
        await AddAsync(admin, child.GuardianId, "side light");
        _api.Face.IdentifyCalls.Clear();

        var verification = await admin.VerifyFaceAsync(_api, child);

        Assert.True(verification > 0);
        var candidate = Assert.Single(Assert.Single(_api.Face.IdentifyCalls));
        Assert.Equal(child.GuardianId, candidate.GuardianId);
        Assert.Equal(2, candidate.References.Count);
    }

    [Fact]
    public async Task WithOneFace_VerificationIsUnchanged()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        _api.Face.IdentifyCalls.Clear();

        await admin.VerifyFaceAsync(_api, child);

        Assert.Empty(_api.Face.IdentifyCalls);
    }

    [Fact]
    public async Task Endpoints_RequireAnAdmin()
    {
        var child = await _api.NewChildAsync();
        var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(child.GuardianId))).StatusCode);
        var guardian = _api.GuardianClient(child.GuardianId);
        Assert.Contains((await guardian.GetAsync(Url(child.GuardianId))).StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task EveryChangeIsAudited_WithoutEmbeddings()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        await AddAsync(admin, child.GuardianId);
        await AddAsync(admin, child.GuardianId, "x");

        await using var db = _api.NewDb();
        var rows = await db.FaceAuditLogs.Where(l => l.GuardianId == child.GuardianId && l.Layer == ChildGuard.Domain.Enums.FaceAuditLayer.Reference).ToListAsync();
        Assert.Equal(2, rows.Count(r => r.Outcome == "Added"));
        Assert.All(rows, r => Assert.DoesNotContain("0.5", r.Detail ?? ""));
        Assert.All(rows, r => Assert.Equal("Admin", r.ActorRole));
    }
}

/// <summary>The guardian app's own routes: a guardian reaches only their own faces.</summary>
public class GuardianOwnFaceReferenceApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public GuardianOwnFaceReferenceApiTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task AGuardian_AddsAndListsOnlyTheirOwnReferences()
    {
        var a = await _api.NewChildAsync(withFace: false);
        var b = await _api.NewChildAsync(withFace: false);
        var guardianA = _api.GuardianClient(a.GuardianId);
        var guardianB = _api.GuardianClient(b.GuardianId);

        (await guardianA.PostAsJsonAsync("/api/me/face-references", new AddFaceReferenceRequest("aW1hZ2U="), Json.Options)).EnsureSuccessStatusCode();
        (await guardianA.PostAsJsonAsync("/api/me/face-references", new AddFaceReferenceRequest("aW1hZ2U=", "Home"), Json.Options)).EnsureSuccessStatusCode();

        var mine = (await guardianA.GetFromJsonAsync<GuardianFaceReferencesDto>("/api/me/face-references", Json.Options))!;
        var theirs = (await guardianB.GetFromJsonAsync<GuardianFaceReferencesDto>("/api/me/face-references", Json.Options))!;
        Assert.Equal(2, mine.References.Count);
        Assert.Empty(theirs.References);
    }

    [Fact]
    public async Task AGuardian_CannotRemoveAnotherGuardiansReference()
    {
        var a = await _api.NewChildAsync(withFace: false);
        var b = await _api.NewChildAsync(withFace: false);
        var guardianA = _api.GuardianClient(a.GuardianId);
        var guardianB = _api.GuardianClient(b.GuardianId);
        foreach (var g in new[] { guardianA, guardianB })
        {
            await g.PostAsJsonAsync("/api/me/face-references", new AddFaceReferenceRequest("aW1hZ2U="), Json.Options);
            await g.PostAsJsonAsync("/api/me/face-references", new AddFaceReferenceRequest("aW1hZ2U="), Json.Options);
        }
        await using var db = _api.NewDb();
        var theirs = await db.GuardianFaceReferences.SingleAsync(r => r.GuardianId == b.GuardianId);

        var response = await guardianA.DeleteAsync($"/api/me/face-references/{theirs.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var after = _api.NewDb();
        Assert.True((await after.GuardianFaceReferences.FindAsync(theirs.Id))!.IsActive);
    }

    [Fact]
    public async Task Analyze_ReportsQuality_AndIsGuardianOnly()
    {
        var a = await _api.NewChildAsync(withFace: false);
        var response = await _api.GuardianClient(a.GuardianId)
            .PostAsJsonAsync("/api/me/face-references/analyze", new AnalyzeImageRequest("aW1hZ2U=", "registration"), Json.Options);

        response.EnsureSuccessStatusCode();
        Assert.True((await response.Content.ReadFromJsonAsync<ImageAnalysisResultDto>(Json.Options))!.Passed);

        var admin = await _api.AdminClientAsync();
        var asAdmin = await admin.PostAsJsonAsync("/api/me/face-references/analyze", new AnalyzeImageRequest("aW1hZ2U="), Json.Options);
        Assert.Contains(asAdmin.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });
    }
}

public sealed class EncryptedFactory : ApiFactory
{
    public string Key { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    protected override IDictionary<string, string> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["Biometrics__EncryptionKey"] = Key;
            return settings;
        }
    }
}

/// <summary>With a key configured, new embeddings are encrypted at rest, and old plain rows keep working.</summary>
public class EncryptedAtRestApiTests : IClassFixture<EncryptedFactory>
{
    private readonly EncryptedFactory _api;
    public EncryptedAtRestApiTests(EncryptedFactory api) => _api = api;

    [Fact]
    public async Task NewFaces_AreStoredEncrypted_AndTheFaceServiceStillSeesReadableData()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);

        (await admin.PostAsJsonAsync($"/api/guardians/{child.GuardianId}/face-registration", new RegisterFaceRequest("aW1hZ2U="), Json.Options))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/guardians/{child.GuardianId}/face-references", new AddFaceReferenceRequest("aW1hZ2U="), Json.Options))
            .EnsureSuccessStatusCode();

        await using var db = _api.NewDb();
        var primary = await db.GuardianFaceData.SingleAsync(f => f.GuardianId == child.GuardianId);
        Assert.StartsWith("enc:v1:k1:", primary.EmbeddingJson);
        Assert.DoesNotContain("0.1,0.2,0.3", primary.EmbeddingJson);
        var extra = await db.GuardianFaceReferences.SingleAsync(r => r.GuardianId == child.GuardianId);
        Assert.StartsWith("enc:v1:k1:", extra.EmbeddingJson);
    }

    [Fact]
    public async Task SingleEncryptedFace_IsDecryptedBeforeItIsSentToTheFaceService()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);
        await admin.PostAsJsonAsync($"/api/guardians/{child.GuardianId}/face-registration", new RegisterFaceRequest("aW1hZ2U="), Json.Options);

        await admin.VerifyFaceAsync(_api, child);

        Assert.Equal("[0.1,0.2,0.3]", _api.Face.LastStoredEmbedding);
    }

    [Fact]
    public async Task AnOldPlainRow_StillVerifies_WhenEncryptionIsOn()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(); // seeded with a plain embedding, as before encryption existed

        var id = await admin.VerifyFaceAsync(_api, child);

        Assert.True(id > 0);
        Assert.Equal("[0.1,0.2,0.3]", _api.Face.LastStoredEmbedding);
    }

    [Fact]
    public async Task ARowEncryptedForAnotherGuardian_IsRefusedAndNothingIsRecorded()
    {
        var admin = await _api.AdminClientAsync();
        var one = await _api.NewChildAsync(withFace: false);
        var two = await _api.NewChildAsync(withFace: false);
        foreach (var c in new[] { one, two })
            await admin.PostAsJsonAsync($"/api/guardians/{c.GuardianId}/face-registration", new RegisterFaceRequest("aW1hZ2U="), Json.Options);

        // Someone with database access copies guardian two's encrypted row onto guardian one.
        await using (var db = _api.NewDb())
        {
            var source = await db.GuardianFaceData.SingleAsync(f => f.GuardianId == two.GuardianId);
            var target = await db.GuardianFaceData.SingleAsync(f => f.GuardianId == one.GuardianId);
            target.EmbeddingJson = source.EmbeddingJson;
            await db.SaveChangesAsync();
        }

        _api.Face.NextMatch = true;
        var response = await admin.PostAsJsonAsync("/api/verification/face-verify",
            new FaceVerifyRequest(one.StudentId, one.GuardianId, "aW1hZ2U="), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var check = _api.NewDb();
        Assert.Equal(0, await check.GuardianVerifications.CountAsync(v => v.GuardianId == one.GuardianId));
        Assert.True(await check.FaceAuditLogs.AnyAsync(l => l.GuardianId == one.GuardianId && l.Outcome == "Failed"));
    }
}
