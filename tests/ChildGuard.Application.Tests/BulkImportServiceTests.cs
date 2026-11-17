using System.Text;
using System.Text.Json;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Import;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Application.Validators;
using ChildGuard.Domain.Enums;
using K = ChildGuard.Application.Import.ImportSchema.Keys;

namespace ChildGuard.Application.Tests;

/// <summary>
/// Bulk import validation and commit: existing single-record rules reused, duplicates found
/// within the file and against the database, references resolved, formula injection refused,
/// and the commit all-or-nothing with a history row either way.
/// </summary>
public class BulkImportServiceTests
{
    private const int AdminId = 7;

    private sealed class Harness
    {
        public FakeBulkImportRepository Repo { get; } = new();
        public FakeEmailSender Email { get; } = new();
        public BulkImportService Service { get; }

        public Harness()
        {
            var (clock, _) = TestClock.Create();
            ImportHandler[] handlers =
            {
                new StudentImportHandler(Repo, new CreateStudentRequestValidator()),
                new GuardianImportHandler(Repo, new CreateGuardianRequestValidator(), new FakePasswordHasher(), Email),
                new DriverImportHandler(Repo, new CreateDriverRequestValidator()),
                new RouteImportHandler(Repo, new CreateRouteRequestValidator()),
                new BusImportHandler(Repo, new CreateBusRequestValidator()),
            };
            Service = new BulkImportService(handlers, Repo, clock);
        }

        public async Task<ImportPreviewDto> ValidateAsync(ImportCategory category, params ImportRowDto[] rows)
        {
            var result = await Service.ValidateAsync(new ValidateImportRequest(category, rows.ToList()));
            Assert.True(result.Succeeded, result.Error);
            return result.Value!;
        }

        public async Task<ImportSummaryDto> CommitAsync(ImportCategory category, bool skipInvalid, params ImportRowDto[] rows)
        {
            var result = await Service.CommitAsync(new CommitImportRequest(category, ImportSource.Csv, "C:\\fakepath\\data.csv", rows.ToList(), skipInvalid), AdminId);
            Assert.True(result.Succeeded, result.Error);
            return result.Value!;
        }
    }

    private static ImportRowDto Row(int number, params (string Key, string? Value)[] values) =>
        new(number, values.ToDictionary(v => v.Key, v => v.Value));

    private static ImportRowDto Guardian(int n, string name, string email, string phone) =>
        Row(n, (K.FullName, name), (K.Email, email), (K.ContactNumber, phone));

    private static ImportRowResultDto RowResult(ImportPreviewDto p, int number) => p.Rows.Single(r => r.RowNumber == number);

    // ---------------------------------------------------------------- guardians

    [Fact]
    public async Task Guardians_DuplicatesAgainstDatabaseAndWithinFileAreReported()
    {
        var h = new Harness();
        h.Repo.Guardians.Add(new GuardianImportKey(1, "Taken@Example.com", "+919820000001"));

        var preview = await h.ValidateAsync(ImportCategory.Guardians,
            Guardian(2, "A", "taken@example.com", "9820011111"),
            Guardian(3, "B", "new@example.com", "+91 98200 00001"),
            Guardian(4, "C", "dup@example.com", "9820022222"),
            Guardian(5, "D", "DUP@example.com", "98200-22222"),
            Guardian(6, "E", "ok@example.com", "9820033333"));

        Assert.Contains(RowResult(preview, 2).Errors, e => e.Column == K.Email && e.Message.Contains("already registered"));
        Assert.Contains(RowResult(preview, 3).Errors, e => e.Column == K.ContactNumber && e.Message.Contains("already registered"));
        Assert.True(RowResult(preview, 4).IsValid);
        var dup = RowResult(preview, 5);
        Assert.Contains(dup.Errors, e => e.Column == K.Email && e.Message.Contains("row 4"));
        Assert.Contains(dup.Errors, e => e.Column == K.ContactNumber && e.Message.Contains("row 4"));
        Assert.True(RowResult(preview, 6).IsValid);
        Assert.Equal((5, 2, 3), (preview.TotalRows, preview.ValidRows, preview.InvalidRows));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("two words@example.com")]
    public async Task Guardians_InvalidEmailIsRejected(string email)
    {
        var preview = await new Harness().ValidateAsync(ImportCategory.Guardians, Guardian(2, "A", email, "9820011111"));
        Assert.Contains(preview.Rows[0].Errors, e => e.Column == K.Email);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("98200abc11")]
    [InlineData("1234567890123456")]
    public async Task Guardians_InvalidPhoneIsRejected(string phone)
    {
        var preview = await new Harness().ValidateAsync(ImportCategory.Guardians, Guardian(2, "A", "a@example.com", phone));
        Assert.Contains(preview.Rows[0].Errors, e => e.Column == K.ContactNumber);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\",\"x\")")]
    [InlineData("@SUM(A1)")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    public async Task FormulaInjectionInTextColumnsIsRejected(string value)
    {
        var preview = await new Harness().ValidateAsync(ImportCategory.Guardians, Guardian(2, value, "a@example.com", "9820011111"));
        Assert.Contains(preview.Rows[0].Errors, e => e.Column == K.FullName && e.Message.Contains("formula"));
    }

    [Fact]
    public async Task RequiredFieldsAndEmptyRowsAreReported()
    {
        var preview = await new Harness().ValidateAsync(ImportCategory.Guardians,
            Guardian(2, "  ", "a@example.com", "9820011111"),
            Row(3, (K.FullName, null)));

        Assert.Contains(RowResult(preview, 2).Errors, e => e.Column == K.FullName && e.Message == "Full Name is required.");
        Assert.Contains(RowResult(preview, 3).Errors, e => e.Column is null && e.Message.Contains("empty"));
    }

    [Fact]
    public async Task Guardians_CommitCreatesAccountsLikeSingleCreationAndEmailsAfterSaving()
    {
        var h = new Harness();

        var summary = await h.CommitAsync(ImportCategory.Guardians, true,
            Guardian(2, "Priya Sharma", "priya@example.com", "+91 98200 11111"),
            Guardian(3, "Ravi Kumar", "ravi@example.com", "9820022222"));

        Assert.Equal(ImportStatus.Completed, summary.Status);
        var saved = Assert.Single(h.Repo.Saved).Guardians;
        Assert.Equal(2, saved.Count);
        Assert.All(saved, g =>
        {
            Assert.True(g.PasswordResetRequired);
            Assert.Equal(RecordStatus.Active, g.Status);
            Assert.StartsWith("hashed:", g.PasswordHash);
        });
        Assert.Equal("+919820011111", saved[0].Phone); // normalized like single creation
        Assert.NotEqual(saved[0].PasswordHash, saved[1].PasswordHash);

        Assert.Equal(new[] { "priya@example.com", "ravi@example.com" }, h.Email.Sent.Select(m => m.ToEmail));
        var password = saved[0].PasswordHash["hashed:".Length..];
        Assert.Contains(password, h.Email.Sent[0].TextBody);
    }

    [Fact]
    public async Task Guardians_UndeliveredEmailIsReportedAndRecordedButAccountsStay()
    {
        var h = new Harness();
        h.Email.FailWith = "SMTP down";

        var summary = await h.CommitAsync(ImportCategory.Guardians, true, Guardian(2, "Priya", "priya@example.com", "9820011111"));

        Assert.Equal(1, summary.InsertedRows);
        var failure = Assert.Single(summary.EmailFailures);
        Assert.Equal((2, "priya@example.com", "SMTP down"), (failure.RowNumber, failure.Email, failure.Error));
        Assert.Contains("could not be delivered", h.Repo.History.Single().Message);
        Assert.Equal(1, h.Repo.Updates);
    }

    // ---------------------------------------------------------------- students

    [Fact]
    public async Task Students_ResolveGuardianBusAndCardAndBuildOneGraph()
    {
        var h = new Harness();
        h.Repo.Guardians.Add(new GuardianImportKey(11, "Mum@Example.com", "9820011111"));
        h.Repo.Buses.Add(new BusImportKey(21, "BUS-12", 31));

        var summary = await h.CommitAsync(ImportCategory.Students, true, Row(2,
            (K.FullName, " Aarav  Sharma "), (K.DateOfBirth, "21/04/2016"), (K.Grade, "Grade 3"),
            (K.GuardianEmail, "mum@example.com"), (K.Relationship, "Mother"), (K.AuthorizedForPickup, "yes"),
            (K.BusNumber, "bus-12"), (K.RfidCardUid, "04A1B2")));

        Assert.Equal(ImportStatus.Completed, summary.Status);
        var student = Assert.Single(Assert.Single(h.Repo.Saved).Students);
        Assert.Equal("Aarav  Sharma", student.FullName);
        Assert.Equal(new DateTime(2016, 4, 21), student.DateOfBirth);
        var link = Assert.Single(student.StudentGuardians);
        Assert.Equal((11, "Mother", true), (link.GuardianId, link.Relationship, link.IsAuthorizedForPickup));
        var assignment = Assert.Single(student.BusAssignments);
        Assert.Equal((21, (int?)31), (assignment.BusId, assignment.RouteId));
        Assert.Equal(("04A1B2", RfidCardStatus.Active), (student.RfidCard!.CardUid, student.RfidCard.Status));
    }

    [Fact]
    public async Task Students_ReferenceAndDuplicateProblemsAreReported()
    {
        var h = new Harness();
        h.Repo.Students.Add(new StudentImportKey("Existing Child", new DateTime(2015, 1, 2)));
        h.Repo.CardUids.Add("CARD-1");

        var preview = await h.ValidateAsync(ImportCategory.Students,
            Row(2, (K.FullName, "existing child"), (K.DateOfBirth, "2015-01-02")),
            Row(3, (K.FullName, "New"), (K.DateOfBirth, "2015-01-02"), (K.GuardianEmail, "nobody@example.com")),
            Row(4, (K.FullName, "New2"), (K.DateOfBirth, "2015-01-02"), (K.Relationship, "Father")),
            Row(5, (K.FullName, "New3"), (K.DateOfBirth, "2015-01-02"), (K.BusNumber, "NOPE"), (K.RfidCardUid, "card-1")),
            Row(6, (K.FullName, "Twin"), (K.DateOfBirth, "2016-03-03"), (K.RfidCardUid, "C9")),
            Row(7, (K.FullName, "TWIN"), (K.DateOfBirth, "03-03-2016"), (K.RfidCardUid, "c9")),
            Row(8, (K.FullName, "Future"), (K.DateOfBirth, "2099-01-01")),
            Row(9, (K.FullName, "Bad date"), (K.DateOfBirth, "31/02/2016")),
            Row(10, (K.FullName, "Serial"), (K.DateOfBirth, "42370"), (K.AuthorizedForPickup, null)));

        Assert.Contains(RowResult(preview, 2).Errors, e => e.Message.Contains("already exists"));
        Assert.Contains(RowResult(preview, 3).Errors, e => e.Column == K.GuardianEmail);
        Assert.Contains(RowResult(preview, 4).Errors, e => e.Column == K.Relationship);
        Assert.Contains(RowResult(preview, 5).Errors, e => e.Column == K.BusNumber);
        Assert.Contains(RowResult(preview, 5).Errors, e => e.Column == K.RfidCardUid && e.Message.Contains("already registered"));
        Assert.True(RowResult(preview, 6).IsValid);
        Assert.Contains(RowResult(preview, 7).Errors, e => e.Column == K.FullName && e.Message.Contains("row 6"));
        Assert.Contains(RowResult(preview, 7).Errors, e => e.Column == K.RfidCardUid && e.Message.Contains("row 6"));
        Assert.Contains(RowResult(preview, 8).Errors, e => e.Column == K.DateOfBirth && e.Message.Contains("past"));
        Assert.Contains(RowResult(preview, 9).Errors, e => e.Column == K.DateOfBirth && e.Message.Contains("not a valid date"));
        Assert.True(RowResult(preview, 10).IsValid); // Excel serial 42370 = 2016-01-01
    }

    // ---------------------------------------------------------------- transport

    [Fact]
    public async Task Buses_ResolveDriverByLicenceAndRouteByName()
    {
        var h = new Harness();
        h.Repo.Drivers.Add(new DriverImportKey(1, "Ramesh", null, "MH12 2011"));
        h.Repo.Drivers.Add(new DriverImportKey(2, "Twin A", null, "DUP-1"));
        h.Repo.Drivers.Add(new DriverImportKey(3, "Twin B", null, "dup 1"));
        h.Repo.Routes.Add(new RouteImportKey(5, "North Loop"));
        h.Repo.Buses.Add(new BusImportKey(9, "BUS-1", null));

        var preview = await h.ValidateAsync(ImportCategory.Buses,
            Row(2, (K.BusNumber, "BUS-2"), (K.Capacity, "40"), (K.DriverLicenseNumber, "mh122011"), (K.RouteName, "north loop")),
            Row(3, (K.BusNumber, "bus-1"), (K.Capacity, "40")),
            Row(4, (K.BusNumber, "BUS-3"), (K.Capacity, "0"), (K.DriverLicenseNumber, "DUP1")),
            Row(5, (K.BusNumber, "BUS-4"), (K.Capacity, "forty"), (K.RouteName, "Nowhere")));

        Assert.True(RowResult(preview, 2).IsValid);
        Assert.Contains(RowResult(preview, 3).Errors, e => e.Column == K.BusNumber && e.Message.Contains("already exists"));
        Assert.Contains(RowResult(preview, 4).Errors, e => e.Column == K.Capacity);
        Assert.Contains(RowResult(preview, 4).Errors, e => e.Column == K.DriverLicenseNumber && e.Message.Contains("More than one"));
        Assert.Contains(RowResult(preview, 5).Errors, e => e.Column == K.Capacity && e.Message.Contains("whole number"));
        Assert.Contains(RowResult(preview, 5).Errors, e => e.Column == K.RouteName);

        var summary = await h.CommitAsync(ImportCategory.Buses, true, Row(2, (K.BusNumber, "BUS-2"), (K.Capacity, "40"), (K.DriverLicenseNumber, "MH12 2011"), (K.RouteName, "North Loop")));
        var bus = Assert.Single(Assert.Single(h.Repo.Saved).Buses);
        Assert.Equal(("BUS-2", 40, (int?)1, (int?)5), (bus.BusNumber, bus.Capacity, bus.DriverId, bus.RouteId));
        Assert.Equal(ImportStatus.Completed, summary.Status);
    }

    [Fact]
    public async Task DriversAndRoutes_DuplicatesAreReported()
    {
        var h = new Harness();
        h.Repo.Drivers.Add(new DriverImportKey(1, "Ramesh", "9876543210", "LIC-1"));
        h.Repo.Routes.Add(new RouteImportKey(1, "North"));

        var drivers = await h.ValidateAsync(ImportCategory.Drivers,
            Row(2, (K.FullName, "Someone"), (K.LicenseNumber, "lic 1")),
            Row(3, (K.FullName, "ramesh"), (K.Phone, "98765-43210")),
            Row(4, (K.FullName, "Fresh"), (K.Phone, "98765 11111"), (K.LicenseNumber, "NEW-1")));
        Assert.Contains(RowResult(drivers, 2).Errors, e => e.Column == K.LicenseNumber);
        Assert.Contains(RowResult(drivers, 3).Errors, e => e.Column == K.FullName && e.Message.Contains("name and phone"));
        Assert.True(RowResult(drivers, 4).IsValid);

        var routes = await h.ValidateAsync(ImportCategory.Routes,
            Row(2, (K.RouteName, "NORTH")), Row(3, (K.RouteName, "South")), Row(4, (K.RouteName, "south ")));
        Assert.False(RowResult(routes, 2).IsValid);
        Assert.True(RowResult(routes, 3).IsValid);
        Assert.Contains(RowResult(routes, 4).Errors, e => e.Message.Contains("row 3"));
    }

    // ---------------------------------------------------------------- commit semantics

    [Fact]
    public async Task Commit_SkipsInvalidRowsAndKeepsThemInTheErrorReport()
    {
        var h = new Harness();

        var summary = await h.CommitAsync(ImportCategory.Routes, true,
            Row(2, (K.RouteName, "North")), Row(3, (K.RouteName, "=1+1")), Row(4, (K.RouteName, "South")));

        Assert.Equal((ImportStatus.CompletedWithErrors, 3, 2, 1), (summary.Status, summary.TotalRows, summary.InsertedRows, summary.FailedRows));
        Assert.Equal(2, h.Repo.Saved.Single().Routes.Count);
        var history = h.Repo.History.Single();
        Assert.Equal((AdminId, "data.csv", ImportSource.Csv), (history.AdminId, history.FileName, history.Source));
        var reported = JsonSerializer.Deserialize<List<ImportRowResultDto>>(history.ErrorReportJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(3, Assert.Single(reported).RowNumber);

        var report = await h.Service.GetErrorReportAsync(history.DataImportId, "csv");
        var csv = Encoding.UTF8.GetString(report.Value!.Data);
        Assert.Contains("\"'=1+1\"", csv); // the hostile value is neutralised in the download
        Assert.Contains("Route Name:", csv);
    }

    [Fact]
    public async Task Commit_AllOrNothingWritesNothingWhenAnyRowIsInvalid()
    {
        var h = new Harness();

        var summary = await h.CommitAsync(ImportCategory.Routes, false, Row(2, (K.RouteName, "North")), Row(3, (K.RouteName, null)));

        Assert.Equal((ImportStatus.Failed, 0), (summary.Status, summary.InsertedRows));
        Assert.Empty(h.Repo.Saved);
        Assert.Equal(ImportStatus.Failed, h.Repo.History.Single().Status);
    }

    [Fact]
    public async Task Commit_ValidatesAgainSoAPreviewCannotBeTrusted()
    {
        var h = new Harness();
        var rows = new[] { Row(2, (K.RouteName, "North")) };
        Assert.True((await h.ValidateAsync(ImportCategory.Routes, rows)).Rows[0].IsValid);

        h.Repo.Routes.Add(new RouteImportKey(1, "North")); // someone added it in the meantime
        var summary = await h.CommitAsync(ImportCategory.Routes, true, rows);

        Assert.Equal(ImportStatus.Failed, summary.Status);
        Assert.Empty(h.Repo.Saved);
    }

    [Fact]
    public async Task Commit_UniqueConflictAtSaveTimeIsRecordedAsFailed()
    {
        var h = new Harness();
        h.Repo.FailNextSaveWithConflict = true;

        var summary = await h.CommitAsync(ImportCategory.Guardians, true, Guardian(2, "A", "a@example.com", "9820011111"));

        Assert.Equal((ImportStatus.Failed, 0), (summary.Status, summary.InsertedRows));
        Assert.Contains("someone else", summary.Message);
        Assert.Empty(h.Email.Sent); // nothing was created, so no credentials go out
        Assert.Equal(ImportStatus.Failed, h.Repo.History.Single().Status);
    }

    [Fact]
    public async Task Requests_AreBoundedAndWellFormed()
    {
        var h = new Harness();
        Assert.False((await h.Service.ValidateAsync(new ValidateImportRequest(ImportCategory.Routes, new()))).Succeeded);
        Assert.False((await h.Service.ValidateAsync(new ValidateImportRequest(ImportCategory.Routes,
            new() { Row(2, (K.RouteName, "A")), Row(2, (K.RouteName, "B")) }))).Succeeded);
        var tooMany = Enumerable.Range(2, ImportSchema.GuardianMaxRows + 1).Select(i => Guardian(i, "G", $"g{i}@example.com", "9820011111")).ToList();
        Assert.False((await h.Service.CommitAsync(new CommitImportRequest(ImportCategory.Guardians, ImportSource.Manual, null, tooMany), AdminId)).Succeeded);
        Assert.False((await h.Service.ValidateAsync(new ValidateImportRequest((ImportCategory)99, new() { Row(2) }))).Succeeded);
    }

    [Fact]
    public async Task Templates_CarryTheHeaderOnlyAndReadBackAsAnEmptyImport()
    {
        var h = new Harness();
        foreach (var category in Enum.GetValues<ImportCategory>())
        {
            var xlsx = h.Service.GetTemplate(category, "xlsx").Value!;
            var header = XlsxFormat.Read(xlsx.Data);
            Assert.Single(header); // the Instructions sheet is never read as data
            Assert.Equal(ImportSchema.Find(category)!.Columns.Select(c => c.Header), header[0].Cells);

            var csv = h.Service.GetTemplate(category, "csv").Value!;
            Assert.EndsWith(".csv", csv.FileName);
            Assert.Single(CsvFormat.Read(csv.Data));
        }
        Assert.False(h.Service.GetTemplate(ImportCategory.Routes, "pdf").Succeeded);
    }

    [Fact]
    public async Task PreviewFile_ReadsAnUploadedWorkbookEndToEnd()
    {
        var h = new Harness();
        var bytes = XlsxFormat.Write(new[]
        {
            new XlsxSheet("Routes", new[]
            {
                new string?[] { "Route Name", "Description" },
                new string?[] { "North", "Market" },
                new string?[] { null, null },
                new string?[] { "north", null },
            })
        });

        var result = await h.Service.PreviewFileAsync(ImportCategory.Routes, bytes, "routes.xlsx");

        Assert.True(result.Succeeded, result.Error);
        var p = result.Value!;
        Assert.Equal((ImportSource.Excel, "routes.xlsx", 2, 1), (p.Source, p.FileName, p.TotalRows, p.ValidRows));
        Assert.Equal(new[] { 2, 4 }, p.Rows.Select(r => r.RowNumber));

        var refused = await h.Service.PreviewFileAsync(ImportCategory.Routes, Encoding.UTF8.GetBytes("x"), "routes.pdf");
        Assert.False(refused.Succeeded);
    }
}
