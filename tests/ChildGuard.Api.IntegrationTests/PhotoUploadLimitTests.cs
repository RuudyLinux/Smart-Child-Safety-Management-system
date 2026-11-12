using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Photos are stored in the database, so a single row cannot exceed the server's
/// max_allowed_packet (XAMPP ships it at 1 MB, below the app's 4 MB limit). A photo
/// the server refuses must come back as a clear "too large" answer, never a 500 that
/// leaves the admin with "An unexpected error occurred" and no photo.
/// </summary>
public class PhotoUploadLimitTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public PhotoUploadLimitTests(ApiFactory api) => _api = api;

    private static ByteArrayContent Png(int bytes)
    {
        var data = new byte[bytes];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return file;
    }

    [Fact]
    public async Task PhotoLargerThanTheDatabasePacketLimit_IsRefusedClearly_NotA500()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        const int size = 1536 * 1024; // a typical phone photo

        long packetLimit;
        await using (var db = _api.NewDb())
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT @@max_allowed_packet";
            packetLimit = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        using var form = new MultipartFormDataContent { { Png(size), "file", "photo.png" } };
        var response = await admin.PostAsync($"/api/students/{child.StudentId}/photo", form);

        if (packetLimit > size + 64 * 1024)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return;
        }
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Contains("too large", body.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }
}
