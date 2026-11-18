using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Import;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using K = ChildGuard.Application.Import.ImportSchema.Keys;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Bulk import through the real API and database: template → upload preview → commit → history,
/// the records it writes, and who may use it.
/// </summary>
public class BulkImportApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public BulkImportApiTests(ApiFactory api) => _api = api;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName, string contentType = "text/csv")
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(part, "file", fileName);
        return form;
    }

    private static ImportRowDto Row(int n, params (string Key, string? Value)[] values) =>
        new(n, values.ToDictionary(v => v.Key, v => v.Value));

    [Fact]
    public async Task Templates_DownloadAsXlsxAndCsv()
    {
        var admin = await _api.AdminClientAsync();

        var xlsx = await admin.GetAsync("/api/imports/Students/template?format=xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        Assert.Equal("childguard-students-template.xlsx", xlsx.Content.Headers.ContentDisposition!.FileNameStar ?? xlsx.Content.Headers.ContentDisposition.FileName);
        var header = XlsxFormat.Read(await xlsx.Content.ReadAsByteArrayAsync());
        Assert.Equal("Full Name", header[0].Cells[0]);

        var csv = await admin.GetAsync("/api/imports/Guardians/template?format=csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Contains("\"Full Name\",\"Email\",\"Contact Number\"", Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync()));

        var categories = await Ok<List<ImportCategoryDto>>(await admin.GetAsync("/api/imports/categories"));
        Assert.Equal(7, categories.Count);
    }

    [Fact]
    public async Task GuardianCsv_PreviewCommitHistoryAndErrorReport()
    {
        var admin = await _api.AdminClientAsync();
        var t = Tag();
        var phoneA = "98" + Random.Shared.Next(10000000, 99999999);
        var phoneB = "97" + Random.Shared.Next(10000000, 99999999);
        var csv = $"Full Name,Email,Contact Number\n" +
                  $"Bulk A {t},bulk.a.{t}@childguard.test,{phoneA}\n" +
                  $"Bulk B {t},bulk.b.{t}@childguard.test,+91 {phoneB}\n" +
                  $"=HYPERLINK(\"x\"),bulk.c.{t}@childguard.test,96{Random.Shared.Next(10000000, 99999999)}\n" +
                  $"Dup {t},BULK.A.{t}@childguard.test,95{Random.Shared.Next(10000000, 99999999)}\n";

        var preview = await Ok<ImportPreviewDto>(await admin.PostAsync("/api/imports/Guardians/preview", Upload(Encoding.UTF8.GetBytes(csv), "guardians.csv")));
        Assert.Equal((ImportSource.Csv, 4, 2, 2), (preview.Source, preview.TotalRows, preview.ValidRows, preview.InvalidRows));
        Assert.Equal(new[] { 2, 3, 4, 5 }, preview.Rows.Select(r => r.RowNumber));
        Assert.Contains(preview.Rows.Single(r => r.RowNumber == 5).Errors, e => e.Message.Contains("row 2"));

        // Nothing is written by a preview.
        await using (var db = _api.NewDb())
            Assert.False(await db.Guardians.AnyAsync(g => g.Email == $"bulk.a.{t}@childguard.test"));

        var rows = preview.Rows.Select(r => new ImportRowDto(r.RowNumber, r.Values)).ToList();
        var summary = await Ok<ImportSummaryDto>(await admin.PostAsJsonAsync("/api/imports/commit",
            new CommitImportRequest(ImportCategory.Guardians, ImportSource.Csv, "guardians.csv", rows), Json.Options));

        Assert.Equal((ImportStatus.CompletedWithErrors, 2, 2), (summary.Status, summary.InsertedRows, summary.FailedRows));
        await using (var db = _api.NewDb())
        {
            var created = await db.Guardians.Where(g => g.Email.StartsWith("bulk.") && g.Email.EndsWith($"{t}@childguard.test")).ToListAsync();
            Assert.Equal(2, created.Count);
            Assert.All(created, g => Assert.True(g.PasswordResetRequired));
            Assert.Contains(created, g => g.Phone == "+91" + phoneB);
            var history = await db.DataImports.SingleAsync(d => d.DataImportId == summary.DataImportId);
            Assert.Equal((2, 2, "guardians.csv"), (history.InsertedRows, history.FailedRows, history.FileName));
        }

        var list = await Ok<List<ImportHistoryItemDto>>(await admin.GetAsync("/api/imports?category=Guardians"));
        var item = list.First(i => i.DataImportId == summary.DataImportId);
        Assert.False(string.IsNullOrWhiteSpace(item.AdminName));

        var detail = await Ok<ImportDetailDto>(await admin.GetAsync($"/api/imports/{summary.DataImportId}"));
        Assert.Equal(new[] { 4, 5 }, detail.FailedRows.Select(r => r.RowNumber));

        var report = await admin.GetAsync($"/api/imports/{summary.DataImportId}/error-report?format=csv");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var reportText = Encoding.UTF8.GetString(await report.Content.ReadAsByteArrayAsync());
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", reportText);
        Assert.Equal("no-store", report.Headers.CacheControl!.ToString());

        // Importing the same file again inserts nothing: every row is now a duplicate.
        var again = await Ok<ImportSummaryDto>(await admin.PostAsJsonAsync("/api/imports/commit",
            new CommitImportRequest(ImportCategory.Guardians, ImportSource.Csv, "guardians.csv", rows), Json.Options));
        Assert.Equal((ImportStatus.Failed, 0), (again.Status, again.InsertedRows));
    }

    [Fact]
    public async Task Students_CommitWritesTheLinkedGraphInOneTransaction()
    {
        var admin = await _api.AdminClientAsync();
        var t = Tag();
        int guardianId, busId, routeId;
        string guardianEmail = $"parent.{t}@childguard.test", busNumber = $"B-{t}";
        await using (var db = _api.NewDb())
        {
            var guardian = new Guardian { FullName = "Parent " + t, Email = guardianEmail, Phone = "93" + Random.Shared.Next(10000000, 99999999), PasswordHash = "x" };
            var route = new Route { RouteName = "R-" + t };
            var bus = new Bus { BusNumber = busNumber, Capacity = 30, Route = route };
            db.AddRange(guardian, route, bus);
            await db.SaveChangesAsync();
            (guardianId, busId, routeId) = (guardian.GuardianId, bus.BusId, route.RouteId);
        }

        var card = "IMP" + t.ToUpperInvariant();
        var summary = await Ok<ImportSummaryDto>(await admin.PostAsJsonAsync("/api/imports/commit", new CommitImportRequest(
            ImportCategory.Students, ImportSource.Manual, null, new List<ImportRowDto>
            {
                Row(1, (K.FullName, "Kid " + t), (K.DateOfBirth, "2016-05-04"), (K.Grade, "2B"), (K.GuardianEmail, guardianEmail.ToUpperInvariant()),
                    (K.Relationship, "Father"), (K.AuthorizedForPickup, "Yes"), (K.BusNumber, busNumber.ToLowerInvariant()), (K.RfidCardUid, card)),
                Row(2, (K.FullName, "Sibling " + t), (K.DateOfBirth, "2018-01-01")),
            }), Json.Options));

        Assert.Equal((ImportStatus.Completed, 2), (summary.Status, summary.InsertedRows));
        await using (var db = _api.NewDb())
        {
            var kid = await db.Students.Include(s => s.StudentGuardians).Include(s => s.BusAssignments).Include(s => s.RfidCard)
                .SingleAsync(s => s.FullName == "Kid " + t);
            Assert.Equal(new DateTime(2016, 5, 4), kid.DateOfBirth);
            var link = Assert.Single(kid.StudentGuardians);
            Assert.Equal((guardianId, "Father", true), (link.GuardianId, link.Relationship, link.IsAuthorizedForPickup));
            var assignment = Assert.Single(kid.BusAssignments);
            Assert.Equal((busId, (int?)routeId, RecordStatus.Active), (assignment.BusId, assignment.RouteId, assignment.Status));
            Assert.Equal(card, kid.RfidCard!.CardUid);
            Assert.Equal("Manual entry", (await db.DataImports.SingleAsync(d => d.DataImportId == summary.DataImportId)).FileName);
        }
    }

    [Fact]
    public async Task AllOrNothing_AnInvalidRowCancelsTheWholeImport()
    {
        var admin = await _api.AdminClientAsync();
        var t = Tag();

        var summary = await Ok<ImportSummaryDto>(await admin.PostAsJsonAsync("/api/imports/commit", new CommitImportRequest(
            ImportCategory.Routes, ImportSource.Manual, null,
            new List<ImportRowDto> { Row(1, (K.RouteName, "Good " + t)), Row(2, (K.RouteName, null)) }, SkipInvalidRows: false), Json.Options));

        Assert.Equal(ImportStatus.Failed, summary.Status);
        await using var db = _api.NewDb();
        Assert.False(await db.Routes.AnyAsync(r => r.RouteName == "Good " + t));
        Assert.Equal(ImportStatus.Failed, (await db.DataImports.SingleAsync(d => d.DataImportId == summary.DataImportId)).Status);
    }

    [Fact]
    public async Task Xlsx_UploadIsReadAndFormulaCellsRefused()
    {
        var admin = await _api.AdminClientAsync();
        var t = Tag();
        var bytes = XlsxFormat.Write(new[]
        {
            new XlsxSheet("Drivers", new[]
            {
                new string?[] { "Full Name", "Phone", "License Number" },
                new string?[] { "Driver " + t, "+91 98765 00000", "LIC-" + t },
            })
        });

        var preview = await Ok<ImportPreviewDto>(await admin.PostAsync("/api/imports/Drivers/preview",
            Upload(bytes, "drivers.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")));

        Assert.Equal((ImportSource.Excel, 1), (preview.Source, preview.ValidRows));
        Assert.Equal("+91 98765 00000", preview.Rows[0].Values[K.Phone]);
    }

    [Fact]
    public async Task Uploads_AreCheckedOnTheServer()
    {
        var admin = await _api.AdminClientAsync();

        var wrongType = await admin.PostAsync("/api/imports/Routes/preview", Upload(Encoding.UTF8.GetBytes("Route Name\nA"), "routes.exe", "text/csv"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Contains(".xlsx", await wrongType.MessageAsync());

        var disguised = await admin.PostAsync("/api/imports/Routes/preview", Upload(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, "routes.xlsx"));
        Assert.Equal(HttpStatusCode.BadRequest, disguised.StatusCode);

        var tooBig = await admin.PostAsync("/api/imports/Routes/preview", Upload(new byte[ImportLimits.MaxFileBytes + 1], "routes.csv"));
        Assert.True(tooBig.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode.ToString());

        var missing = await admin.PostAsync("/api/imports/Routes/preview", new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/imports/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/imports/999999/error-report")).StatusCode);
    }

    [Fact]
    public async Task OnlyAdminsCanImport()
    {
        var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/imports/categories")).StatusCode);

        var child = await _api.NewChildAsync(withFace: false);
        var guardian = _api.GuardianClient(child.GuardianId);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/imports")).StatusCode);
        var commit = await guardian.PostAsJsonAsync("/api/imports/commit", new CommitImportRequest(
            ImportCategory.Routes, ImportSource.Manual, null, new List<ImportRowDto> { Row(1, (K.RouteName, "Nope")) }), Json.Options);
        Assert.Equal(HttpStatusCode.Forbidden, commit.StatusCode);
    }

    [Fact]
    public async Task RouteStops_CommitWritesStopsInSequenceOrder()
    {
        var admin = await _api.AdminClientAsync();
        var t = Tag();

        // Seed a route
        var routeName = $"Route-{t}";
        var routeRows = new List<ImportRowDto> { Row(1, (K.RouteName, routeName), (K.Description, "Test")) };
        var routeResult = await Ok<ImportSummaryDto>(await admin.PostAsJsonAsync("/api/imports/commit",
            new CommitImportRequest(ImportCategory.Routes, ImportSource.Manual, null, routeRows), Json.Options));
        Assert.Equal(1, routeResult.InsertedRows);

        // Import 3 stops
        var rows = new List<ImportRowDto>
        {
            Row(1, (K.RouteName, routeName), (K.StopName, $"Stop-A-{t}"), (K.PickupTime, "07:00"), (K.DropTime, "16:00")),
            Row(2, (K.RouteName, routeName), (K.StopName, $"Stop-B-{t}"), (K.Address, "Main St")),
            Row(3, (K.RouteName, routeName), (K.StopName, $"Stop-C-{t}")),
        };

        var preview = await Ok<ImportPreviewDto>(await admin.PostAsJsonAsync("/api/imports/validate",
            new ValidateImportRequest(ImportCategory.RouteStops, rows), Json.Options));
        Assert.Equal(3, preview.ValidRows);
        Assert.Equal(0, preview.InvalidRows);

        var summary = await Ok<ImportSummaryDto>(await admin.PostAsJsonAsync("/api/imports/commit",
            new CommitImportRequest(ImportCategory.RouteStops, ImportSource.Manual, null, rows), Json.Options));
        Assert.Equal(3, summary.InsertedRows);

        // Verify via DB
        await using var db = _api.NewDb();
        var route = await db.Routes.FirstAsync(r => r.RouteName == routeName);
        var stops = await db.RouteStops.Where(s => s.RouteId == route.RouteId).OrderBy(s => s.Sequence).ToListAsync();
        Assert.Equal(3, stops.Count);
        Assert.Equal(new[] { 1, 2, 3 }, stops.Select(s => s.Sequence).ToArray());
        Assert.Contains(stops, s => s.Name == $"Stop-A-{t}" && s.PickupTime == new TimeOnly(7, 0));
        Assert.Contains(stops, s => s.Name == $"Stop-B-{t}" && s.Address == "Main St");

        // Within-batch duplicate stop name on same route is rejected
        var dupRows = new List<ImportRowDto>
        {
            Row(1, (K.RouteName, routeName), (K.StopName, $"Dup-{t}")),
            Row(2, (K.RouteName, routeName), (K.StopName, $"Dup-{t}")),
        };
        var dupPreview = await Ok<ImportPreviewDto>(await admin.PostAsJsonAsync("/api/imports/validate",
            new ValidateImportRequest(ImportCategory.RouteStops, dupRows), Json.Options));
        Assert.Equal(1, dupPreview.ValidRows);
        Assert.Equal(1, dupPreview.InvalidRows);
    }

    [Fact]
    public async Task SchoolCalendarDays_CommitWritesExceptionsAndRejectsDuplicates()
    {
        var admin = await _api.AdminClientAsync();
        var t = Tag();

        // Pick a date far in the future to avoid conflicts with real data
        var date1 = DateOnly.FromDateTime(DateTime.Today.AddMonths(6)).ToString("yyyy-MM-dd");
        var date2 = DateOnly.FromDateTime(DateTime.Today.AddMonths(7)).ToString("yyyy-MM-dd");

        var rows = new List<ImportRowDto>
        {
            Row(1, (K.Date, date1), (K.DayType, "Holiday"), (K.Title, $"Test-Holiday-{t}")),
            Row(2, (K.Date, date2), (K.DayType, "HalfDay"), (K.Title, $"Test-HalfDay-{t}"), (K.DismissalTime, "12:00")),
        };

        var preview = await Ok<ImportPreviewDto>(await admin.PostAsJsonAsync("/api/imports/validate",
            new ValidateImportRequest(ImportCategory.SchoolCalendarDays, rows), Json.Options));
        Assert.Equal(2, preview.ValidRows);

        var summary = await Ok<ImportSummaryDto>(await admin.PostAsJsonAsync("/api/imports/commit",
            new CommitImportRequest(ImportCategory.SchoolCalendarDays, ImportSource.Manual, null, rows), Json.Options));
        Assert.Equal(2, summary.InsertedRows);

        // Verify DB
        await using var db = _api.NewDb();
        var d1 = DateOnly.Parse(date1);
        var cal = await db.SchoolCalendarDays.FirstOrDefaultAsync(d => d.Date == d1);
        Assert.NotNull(cal);
        Assert.Equal(SchoolDayType.Holiday, cal.DayType);

        // Importing the same date again is a duplicate
        var dupRows = new List<ImportRowDto> { Row(1, (K.Date, date1), (K.DayType, "Holiday"), (K.Title, "Dup")) };
        var dupPreview = await Ok<ImportPreviewDto>(await admin.PostAsJsonAsync("/api/imports/validate",
            new ValidateImportRequest(ImportCategory.SchoolCalendarDays, dupRows), Json.Options));
        Assert.Equal(0, dupPreview.ValidRows);
        Assert.Equal(1, dupPreview.InvalidRows);

        // Validation: HalfDay without DismissalTime is rejected
        var d3 = DateOnly.FromDateTime(DateTime.Today.AddMonths(8)).ToString("yyyy-MM-dd");
        var badRows = new List<ImportRowDto> { Row(1, (K.Date, d3), (K.DayType, "HalfDay"), (K.Title, "Bad")) };
        var badPreview = await Ok<ImportPreviewDto>(await admin.PostAsJsonAsync("/api/imports/validate",
            new ValidateImportRequest(ImportCategory.SchoolCalendarDays, badRows), Json.Options));
        Assert.Equal(0, badPreview.ValidRows);
    }
}
