using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Export, import and reset of the whole database, and the demo school the reset loads. Run on MySQL and on
/// PostgreSQL like the rest of the suite. Tests in this class share one database and run one at a time.
/// </summary>
public class DatabaseMaintenanceTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public DatabaseMaintenanceTests(ApiFactory api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private static Task<HttpResponseMessage> Reset(HttpClient admin, bool demo = true, string word = "RESET") =>
        admin.PostAsJsonAsync("/api/admin/database/reset", new DatabaseResetRequest(word, demo), Json.Options);

    private static int Rows(DatabaseOperationResultDto r, string table) => r.Tables.Single(t => t.Table == table).Rows;

    [Fact]
    public async Task Reset_LoadsTheDemoSchool_AndKeepsAdminsAndScanners()
    {
        var admin = await _api.AdminClientAsync();
        await _api.NewChildAsync(withFace: false); // something old to clear
        int devicesBefore, adminsBefore;
        await using (var db = _api.NewDb())
            (devicesBefore, adminsBefore) = (await db.RfidDevices.CountAsync(), await db.Admins.CountAsync());

        var result = await Ok<DatabaseOperationResultDto>(await Reset(admin));
        Assert.Equal(15, Rows(result, "Students"));
        Assert.Equal(15, Rows(result, "Guardians"));
        Assert.Equal(6, Rows(result, "Drivers"));
        Assert.Equal(5, Rows(result, "Buses"));
        Assert.Equal(5, Rows(result, "Routes"));
        Assert.Equal(38, Rows(result, "RouteStops"));
        Assert.Equal(6, Rows(result, "DriverPhotos"));

        // The admin who reset is still the same account and can still sign in with the same password.
        var again = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/admin/database")).StatusCode);

        await using var check = _api.NewDb();
        Assert.Equal(adminsBefore, await check.Admins.CountAsync());
        Assert.Equal(devicesBefore, await check.RfidDevices.CountAsync());
        Assert.DoesNotContain(await check.Students.Select(s => s.FullName).ToListAsync(), n => n.StartsWith("Test Child"));

        // Every student has a unique admission number and at least one guardian authorised for pickup.
        var students = await check.Students.Include(s => s.StudentGuardians).ToListAsync();
        Assert.All(students, s => Assert.Matches(@"^ADM2026\d{5}$", s.AdmissionNumber));
        Assert.Equal(students.Count, students.Select(s => s.AdmissionNumber).Distinct().Count());
        Assert.All(students, s => Assert.Contains(s.StudentGuardians, g => g.IsAuthorizedForPickup));

        // Every bus on a route has a driver; every rider's stops belong to the route of their bus.
        var buses = await check.Buses.ToListAsync();
        Assert.All(buses, b => { Assert.NotNull(b.DriverId); Assert.NotNull(b.RouteId); });
        var rides = await check.StudentBusAssignments.Include(a => a.Bus).Include(a => a.PickupStop).Include(a => a.DropStop).ToListAsync();
        Assert.Equal(14, rides.Count);
        Assert.All(rides, a =>
        {
            Assert.Equal(a.Bus.RouteId, a.RouteId);
            Assert.Equal(a.RouteId, a.PickupStop!.RouteId);
            Assert.Equal(a.RouteId, a.DropStop!.RouteId);
        });

        // Cards: 14 assigned, one student pending, one card lost and replaced, spares on the shelf.
        Assert.Equal(14,await check.RfidCards.CountAsync(c => c.StudentId != null));
        Assert.Equal(1, await check.RfidCards.CountAsync(c => c.Status == RfidCardStatus.Lost));
        Assert.Equal(4, await check.RfidCards.CountAsync(c => c.StudentId == null && c.Status == RfidCardStatus.Active));

        // History stops before today, so today starts clean for live scanning.
        var todayStartUtc = new DateTime(2026, 11, 5, 18, 30, 0, DateTimeKind.Utc);
        Assert.True(await check.JourneyEvents.AnyAsync());
        Assert.False(await check.JourneyEvents.AnyAsync(e => e.Timestamp >= todayStartUtc));
        Assert.Contains(await check.AuditEntries.Select(a => a.Action).ToListAsync(), a => a == "DatabaseReset");

        // The dashboard and the student list work on the new data.
        var summary = await Ok<DashboardSummaryDto>(await again.GetAsync("/api/dashboard/summary"));
        Assert.Equal(15, summary.TotalStudents);
        Assert.Equal(15, (await Ok<List<StudentListItemDto>>(await again.GetAsync("/api/students"))).Count);
    }

    [Fact]
    public async Task Export_ThenImport_RestoresEveryRecord()
    {
        var admin = await _api.AdminClientAsync();
        await Ok<DatabaseOperationResultDto>(await Reset(admin));
        var before = await Ok<DatabaseOverviewDto>(await admin.GetAsync("/api/admin/database"));

        var export = await admin.GetAsync("/api/admin/database/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/json", export.Content.Headers.ContentType!.MediaType);
        var backup = await export.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain(_api.AdminPassword, System.Text.Encoding.UTF8.GetString(backup));
        Assert.DoesNotContain(ApiFactory.AdminEmail, System.Text.Encoding.UTF8.GetString(backup));

        // Wipe, then restore from the file.
        await Ok<DatabaseOperationResultDto>(await Reset(admin, demo: false));
        Assert.Equal(0, (await Ok<DatabaseOverviewDto>(await admin.GetAsync("/api/admin/database"))).Tables.Single(t => t.Table == "Students").Rows);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(backup);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(file, "file", "backup.json");
        await Ok<DatabaseOperationResultDto>(await admin.PostAsync("/api/admin/database/import", form));

        var after = await Ok<DatabaseOverviewDto>(await admin.GetAsync("/api/admin/database"));
        foreach (var t in before.Tables.Where(t => t.Table != "AuditEntries"))
            Assert.True(t.Rows == after.Tables.Single(x => x.Table == t.Table).Rows, $"{t.Table}: {t.Rows} before, {after.Tables.Single(x => x.Table == t.Table).Rows} after");

        // Ids are kept, and new records continue after them.
        await using (var db = _api.NewDb())
        {
            var maxId = await db.Students.MaxAsync(s => s.StudentId);
            var created = await Ok<StudentDetailDto>(await admin.PostAsJsonAsync("/api/students",
                new CreateStudentRequest("After Import", new DateTime(2016, 1, 1), "2"), Json.Options));
            Assert.True(created.StudentId > maxId);
        }

        // Photos survive byte for byte.
        var driverId = (await Ok<List<DriverDto>>(await admin.GetAsync("/api/transportation/drivers"))).First().DriverId;
        var photo = await admin.GetAsync($"/api/transportation/drivers/{driverId}/photo");
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/png", photo.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task BadInputs_AreRefused_AndChangeNothing()
    {
        var admin = await _api.AdminClientAsync();
        await Ok<DatabaseOperationResultDto>(await Reset(admin));

        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(admin, word: "reset please")).StatusCode);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("{\"hello\":1}"), "file", "not-a-backup.json");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/database/import", form)).StatusCode);

        Assert.Equal(15, (await Ok<DatabaseOverviewDto>(await admin.GetAsync("/api/admin/database"))).Tables.Single(t => t.Table == "Students").Rows);
    }

    [Fact]
    public async Task DriverPhoto_CanBeUploadedReplacedAndRemoved()
    {
        var admin = await _api.AdminClientAsync();
        var driver = await Ok<DriverDto>(await admin.PostAsJsonAsync("/api/transportation/drivers",
            new CreateDriverRequest("Photo Driver " + Guid.NewGuid().ToString("N")[..6], null, null), Json.Options));
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        Assert.Null((await Ok<DriverDetailDto>(await admin.GetAsync($"/api/transportation/drivers/{driver.DriverId}"))).PhotoUpdatedAt);
        using (var form = new MultipartFormDataContent())
        {
            var part = new ByteArrayContent(png);
            part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(part, "file", "driver.png");
            await Ok<PhotoStateDto>(await admin.PostAsync($"/api/transportation/drivers/{driver.DriverId}/photo", form));
        }
        Assert.NotNull((await Ok<DriverDetailDto>(await admin.GetAsync($"/api/transportation/drivers/{driver.DriverId}"))).PhotoUpdatedAt);
        Assert.Equal(png, await (await admin.GetAsync($"/api/transportation/drivers/{driver.DriverId}/photo")).Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/transportation/drivers/{driver.DriverId}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/transportation/drivers/{driver.DriverId}/photo")).StatusCode);
    }

    [Fact]
    public async Task OnlyAdmins_CanUseIt()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/admin/database/export")).StatusCode);
        var guardian = _api.GuardianClient((await _api.NewChildAsync(withFace: false)).GuardianId);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/admin/database/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.PostAsJsonAsync("/api/admin/database/reset", new DatabaseResetRequest("RESET"), Json.Options)).StatusCode);
    }
}
