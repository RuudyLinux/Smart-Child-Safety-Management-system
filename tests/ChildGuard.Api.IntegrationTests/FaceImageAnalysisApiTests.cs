using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>Layer 1 endpoint: image quality and face detection, for webcam captures and gallery files alike.</summary>
public class FaceImageAnalysisApiTests : IClassFixture<ApiFactory>
{
    private const string Url = "/api/verification/analyze-image";
    private readonly ApiFactory _api;
    public FaceImageAnalysisApiTests(ApiFactory api) => _api = api;

    private static ImageAnalysis WithIssues(string[] issues, string[]? messages = null) =>
        new("verification", issues.Contains("NO_FACE") ? 0 : 1, issues, [], messages ?? [],
            new ImageAnalysisMetrics(300, 150, 40, 200, 0, 0.99));

    [Fact]
    public async Task CleanImage_Passes()
    {
        _api.Face.NextAnalysis = Result<ImageAnalysis>.Success(FakeFaceServiceClient.Clean);
        var admin = await _api.AdminClientAsync();

        var response = await admin.PostAsJsonAsync(Url, new AnalyzeImageRequest("aW1hZ2U="), Json.Options);

        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<ImageAnalysisResultDto>(Json.Options))!;
        Assert.True(body.Passed);
        Assert.Equal(CheckResult.Pass, body.Quality);
        Assert.Equal(CheckResult.Pass, body.FaceDetection);
    }

    [Fact]
    public async Task BlurryImage_ReportsTheReason_InPlainLanguage()
    {
        _api.Face.NextAnalysis = Result<ImageAnalysis>.Success(WithIssues(["BLURRY"], ["The image is too blurry."]));
        var admin = await _api.AdminClientAsync();

        var response = await admin.PostAsJsonAsync(Url, new AnalyzeImageRequest("aW1hZ2U="), Json.Options);

        var body = (await response.Content.ReadFromJsonAsync<ImageAnalysisResultDto>(Json.Options))!;
        Assert.False(body.Passed);
        Assert.Equal(CheckResult.Fail, body.Quality);
        Assert.Equal(CheckResult.Pass, body.FaceDetection);
        Assert.Contains("too blurry", body.Messages.Single());
    }

    [Fact]
    public async Task NoFace_FailsDetection()
    {
        _api.Face.NextAnalysis = Result<ImageAnalysis>.Success(WithIssues(["NO_FACE"]));
        var admin = await _api.AdminClientAsync();

        var response = await admin.PostAsJsonAsync(Url, new AnalyzeImageRequest("aW1hZ2U="), Json.Options);

        var body = (await response.Content.ReadFromJsonAsync<ImageAnalysisResultDto>(Json.Options))!;
        Assert.Equal(CheckResult.Fail, body.FaceDetection);
        Assert.Equal(0, body.FaceCount);
    }

    [Fact]
    public async Task FaceServiceDown_IsA400WithAMessage()
    {
        _api.Face.NextAnalysis = Result<ImageAnalysis>.Failure("Face service is unavailable.");
        var admin = await _api.AdminClientAsync();

        var response = await admin.PostAsJsonAsync(Url, new AnalyzeImageRequest("aW1hZ2U="), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unavailable", await response.Content.ReadAsStringAsync());
        _api.Face.NextAnalysis = Result<ImageAnalysis>.Success(FakeFaceServiceClient.Clean);
    }

    [Fact]
    public async Task WithoutToken_IsUnauthorized()
    {
        var response = await _api.CreateClient().PostAsJsonAsync(Url, new AnalyzeImageRequest("aW1hZ2U="), Json.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GuardianToken_IsForbidden()
    {
        var response = await _api.GuardianClient(1).PostAsJsonAsync(Url, new AnalyzeImageRequest("aW1hZ2U="), Json.Options);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task EmptyImage_IsRejected()
    {
        var admin = await _api.AdminClientAsync();
        var response = await admin.PostAsJsonAsync(Url, new AnalyzeImageRequest(""), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImageTextOverTheLimit_IsRejectedBeforeReachingTheFaceService()
    {
        var admin = await _api.AdminClientAsync();
        var huge = new string('A', AnalyzeImageRequestValidatorLimit + 1);
        var response = await admin.PostAsJsonAsync(Url, new AnalyzeImageRequest(huge), Json.Options);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
    }

    private static int AnalyzeImageRequestValidatorLimit => ChildGuard.Application.Validators.AnalyzeImageRequestValidator.MaxBase64Length;

    [Fact]
    public async Task UnknownPurpose_IsRejected()
    {
        var admin = await _api.AdminClientAsync();
        var response = await admin.PostAsJsonAsync(Url, new AnalyzeImageRequest("aW1hZ2U=", "something"), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EachLayer_IsAudited_WithTheAdmin_AndWithoutTheImage()
    {
        _api.Face.NextAnalysis = Result<ImageAnalysis>.Success(WithIssues(["TOO_DARK"]));
        var admin = await _api.AdminClientAsync();
        var marker = Convert.ToBase64String("audit-marker-should-not-be-stored"u8.ToArray());
        var before = _api.Time.UtcNow.AddMinutes(-1);

        await admin.PostAsJsonAsync(Url, new AnalyzeImageRequest(marker, "registration"), Json.Options);

        await using var db = _api.NewDb();
        var rows = await db.FaceAuditLogs.Where(l => l.OccurredAt >= before && l.Action == "AnalyzeImage").ToListAsync();
        var quality = Assert.Single(rows, r => r.Layer == FaceAuditLayer.Quality && r.Detail!.Contains("purpose=registration"));
        Assert.Equal("Fail", quality.Outcome);
        Assert.Equal("Admin", quality.ActorRole);
        Assert.NotNull(quality.ActorId);
        Assert.Contains(rows, r => r.Layer == FaceAuditLayer.Detection && r.Outcome == "Pass");
        Assert.All(rows, r => Assert.DoesNotContain("audit-marker", r.Detail ?? ""));
        _api.Face.NextAnalysis = Result<ImageAnalysis>.Success(FakeFaceServiceClient.Clean);
    }
}
