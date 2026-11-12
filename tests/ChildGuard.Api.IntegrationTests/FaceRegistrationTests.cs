using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Face ID: admins can register a guardian's face in person (school desk camera), and the
/// pickup desk can tell "no face registered" apart from "face did not match".
/// </summary>
public class FaceRegistrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public FaceRegistrationTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task Verify_WithoutRegisteredFace_SaysSo_AndRecordsNothing()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);

        var response = await admin.PostAsJsonAsync("/api/verification/face-verify",
            new FaceVerifyRequest(child.StudentId, child.GuardianId, "aW1hZ2U="), Json.Options);

        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<FaceVerifyResultDto>(Json.Options))!;
        Assert.False(body.FaceRegistered);
        Assert.False(body.FaceMatched);
        Assert.True(body.GuardianAuthorized);
        Assert.Equal(0, body.VerificationId);
        await using var db = _api.NewDb();
        Assert.Equal(0, await db.GuardianVerifications.CountAsync(v => v.GuardianId == child.GuardianId));
    }

    [Fact]
    public async Task Verify_WithRegisteredFace_ReportsFaceRegistered()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        _api.Face.NextMatch = false;

        var response = await admin.PostAsJsonAsync("/api/verification/face-verify",
            new FaceVerifyRequest(child.StudentId, child.GuardianId, "aW1hZ2U="), Json.Options);

        var body = (await response.Content.ReadFromJsonAsync<FaceVerifyResultDto>(Json.Options))!;
        Assert.True(body.FaceRegistered);
        Assert.False(body.FaceMatched);
        Assert.True(body.VerificationId > 0);
    }

    [Fact]
    public async Task Admin_RegistersGuardianFace_ThenTheGuardianCanBeVerified()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync(withFace: false);

        var register = await admin.PostAsJsonAsync($"/api/guardians/{child.GuardianId}/face-registration",
            new RegisterFaceRequest("aW1hZ2U="), Json.Options);

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var detail = await admin.GetFromJsonAsync<GuardianDetailDto>($"/api/guardians/{child.GuardianId}", Json.Options);
        Assert.True(detail!.FaceRegistered);
        _api.Face.NextMatch = true;
        var verifyId = await admin.VerifyFaceAsync(_api, child);
        Assert.True(verifyId > 0);
    }

    [Fact]
    public async Task Admin_RegisterFace_ForUnknownGuardian_Is404()
    {
        var admin = await _api.AdminClientAsync();
        var response = await admin.PostAsJsonAsync("/api/guardians/999999/face-registration",
            new RegisterFaceRequest("aW1hZ2U="), Json.Options);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_RegisterFace_WithoutImage_Is400()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        var response = await admin.PostAsJsonAsync($"/api/guardians/{child.GuardianId}/face-registration",
            new RegisterFaceRequest(""), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Guardian_CannotUseTheAdminEndpoint_EvenForThemselves()
    {
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        var response = await guardian.PostAsJsonAsync($"/api/guardians/{child.GuardianId}/face-registration",
            new RegisterFaceRequest("aW1hZ2U="), Json.Options);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_CannotRegisterAFace()
    {
        var child = await _api.NewChildAsync();
        var response = await _api.CreateClient().PostAsJsonAsync($"/api/guardians/{child.GuardianId}/face-registration",
            new RegisterFaceRequest("aW1hZ2U="), Json.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
