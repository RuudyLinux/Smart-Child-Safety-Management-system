using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

public class FaceQualityPolicyTests
{
    private static ImageAnalysis Analysis(string[]? issues = null, string[]? warnings = null, int faces = 1) =>
        new("verification", faces, issues ?? [], warnings ?? [], [], new ImageAnalysisMetrics(300, 150, 40, 200, 0, 0.99));

    [Fact]
    public void CleanImage_PassesBothChecks()
    {
        var result = FaceQualityPolicy.Assess(Analysis());
        Assert.Equal(CheckResult.Pass, result.Quality);
        Assert.Equal(CheckResult.Pass, result.FaceDetection);
        Assert.True(result.Passed);
    }

    [Fact]
    public void WarningsAlone_NeverBlock()
    {
        var result = FaceQualityPolicy.Assess(Analysis(warnings: ["FACE_OBSTRUCTED", "LOW_CONTRAST"]));
        Assert.True(result.Passed);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Theory]
    [InlineData("BLURRY")]
    [InlineData("TOO_DARK")]
    [InlineData("TOO_BRIGHT")]
    [InlineData("EXTREME_ANGLE")]
    [InlineData("FACE_OBSTRUCTED")]
    [InlineData("FACE_TOO_SMALL")]
    public void QualityProblem_FailsQuality_ButNotDetection(string code)
    {
        var result = FaceQualityPolicy.Assess(Analysis([code]));
        Assert.Equal(CheckResult.Fail, result.Quality);
        Assert.Equal(CheckResult.Pass, result.FaceDetection);
        Assert.False(result.Passed);
    }

    [Theory]
    [InlineData("NO_FACE")]
    [InlineData("MULTIPLE_FACES")]
    [InlineData("FACE_CUT_OFF")]
    [InlineData("BAD_FORMAT")]
    [InlineData("TOO_LARGE")]
    [InlineData("LOW_RESOLUTION")]
    public void DetectionProblem_FailsBothChecks(string code)
    {
        var result = FaceQualityPolicy.Assess(Analysis([code]));
        Assert.Equal(CheckResult.Fail, result.Quality);
        Assert.Equal(CheckResult.Fail, result.FaceDetection);
        Assert.False(result.Passed);
    }

    [Fact]
    public void UnknownFutureIssueCode_StillBlocks()
    {
        var result = FaceQualityPolicy.Assess(Analysis(["SOMETHING_NEW"]));
        Assert.False(result.Passed);
    }
}

public class FaceImageAnalysisServiceTests
{
    private const string Image = "TOP-SECRET-IMAGE-BYTES";
    private readonly FakeFaceServiceClient _client = new();
    private readonly FakeFaceAuditLog _audit = new();
    private FaceImageAnalysisService Service() => new(_client, _audit);

    [Theory]
    [InlineData(null, "verification")]
    [InlineData("verification", "verification")]
    [InlineData("registration", "registration")]
    [InlineData("REGISTRATION", "registration")]
    [InlineData("anything-else", "verification")]
    public async Task Purpose_IsNormalised(string? requested, string expected)
    {
        await Service().AnalyzeAsync(new AnalyzeImageRequest(Image, requested), FaceActor.Admin(1));
        Assert.Equal(expected, _client.LastPurpose);
    }

    [Fact]
    public async Task Result_RecordsQualityAndDetectionSeparately_WithTheCaller()
    {
        _client.NextAnalysis = Result<ImageAnalysis>.Success(new ImageAnalysis(
            "verification", 1, ["BLURRY"], [], ["The image is too blurry."], new ImageAnalysisMetrics(10, 150, 40, 200, 0, 0.99)));

        var result = await Service().AnalyzeAsync(new AnalyzeImageRequest(Image), FaceActor.Admin(7));

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.Passed);
        Assert.Equal(CheckResult.Fail, result.Value.Quality);
        Assert.Equal(CheckResult.Pass, result.Value.FaceDetection);

        var quality = Assert.Single(_audit.Entries, e => e.Layer == FaceAuditLayer.Quality);
        var detection = Assert.Single(_audit.Entries, e => e.Layer == FaceAuditLayer.Detection);
        Assert.Equal("Fail", quality.Outcome);
        Assert.Equal("Pass", detection.Outcome);
        Assert.Equal("Admin", quality.Actor.Role);
        Assert.Equal(7, quality.Actor.Id);
        Assert.Contains("BLURRY", quality.Detail);
    }

    [Fact]
    public async Task AuditRows_NeverContainTheImage()
    {
        await Service().AnalyzeAsync(new AnalyzeImageRequest(Image), FaceActor.Admin(1));
        Assert.All(_audit.Entries, e => Assert.DoesNotContain(Image, e.Detail ?? ""));
    }

    [Fact]
    public async Task FaceServiceDown_IsAFailure_AndIsAudited()
    {
        _client.NextAnalysis = Result<ImageAnalysis>.Failure("Face service is unavailable.");

        var result = await Service().AnalyzeAsync(new AnalyzeImageRequest(Image), FaceActor.Admin(1));

        Assert.False(result.Succeeded);
        Assert.Equal("Face service is unavailable.", result.Error);
        var row = Assert.Single(_audit.Entries);
        Assert.Equal("Unavailable", row.Outcome);
    }
}
