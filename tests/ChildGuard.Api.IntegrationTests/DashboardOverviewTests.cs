using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Milestone 3E against the real database: the dashboard's people, RFID and fleet KPIs, the coverage gaps, and the
/// alerts for missing guardians or transport, lost cards, driver licences and buses off the road. Every number is
/// checked as a change from the previous summary, since tests in this class share one database.
/// The test clock puts "today" at the school on 6 Nov 2026.
/// </summary>
public class DashboardOverviewTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public DashboardOverviewTests(ApiFactory api) => _api = api;

    private static string Unique(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static async Task<DashboardSummaryDto> SummaryAsync(HttpClient admin)
    {
        var r = await admin.GetAsync("/api/dashboard/summary");
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<DashboardSummaryDto>(Json.Options))!;
    }

    [Fact]
    public async Task PeopleAndRfidCounts_FollowTheRecords()
    {
        var admin = await _api.AdminClientAsync();
        var before = (await SummaryAsync(admin)).Admin!;

        await using (var db = _api.NewDb())
        {
            var active = new Student { FullName = Unique("Dash Active "), DateOfBirth = new DateTime(2016, 1, 1) };
            var inactive = new Student { FullName = Unique("Dash Inactive "), DateOfBirth = new DateTime(2016, 1, 1), Status = RecordStatus.Inactive };
            var archived = new Student
            {
                FullName = Unique("Dash Archived "), DateOfBirth = new DateTime(2016, 1, 1), Status = RecordStatus.Inactive,
                ArchivedAt = DateTime.UtcNow, ArchiveReason = "Left the school in October."
            };
            db.Students.AddRange(active, inactive, archived);
            db.Guardians.Add(new Guardian { FullName = Unique("Dash Guardian "), Email = $"{Unique("g")}@dash.test", PasswordHash = "x" });
            await db.SaveChangesAsync();

            db.RfidCards.AddRange(
                new RfidCard { CardUid = Unique("DASH-A-"), StudentId = active.StudentId, AssignedAt = DateTime.UtcNow },
                new RfidCard { CardUid = Unique("DASH-S-") },
                new RfidCard { CardUid = Unique("DASH-L-"), Status = RfidCardStatus.Lost },
                new RfidCard { CardUid = Unique("DASH-D-"), Status = RfidCardStatus.Damaged },
                new RfidCard { CardUid = Unique("DASH-R-"), Status = RfidCardStatus.Retired });
            await db.SaveChangesAsync();
        }

        var after = (await SummaryAsync(admin)).Admin!;
        Assert.Equal(before.People.Students + 3, after.People.Students);
        Assert.Equal(before.People.ActiveStudents + 1, after.People.ActiveStudents);
        Assert.Equal(before.People.InactiveStudents + 1, after.People.InactiveStudents);
        Assert.Equal(before.People.ArchivedStudents + 1, after.People.ArchivedStudents);
        Assert.Equal(before.People.ActiveGuardians + 1, after.People.ActiveGuardians);

        Assert.Equal(before.Rfid.Total + 5, after.Rfid.Total);
        Assert.Equal(before.Rfid.Assigned + 1, after.Rfid.Assigned);
        Assert.Equal(before.Rfid.Available + 1, after.Rfid.Available);
        Assert.Equal((before.Rfid.Lost + 1, before.Rfid.Damaged + 1, before.Rfid.Retired + 1), (after.Rfid.Lost, after.Rfid.Damaged, after.Rfid.Retired));

        // The new active student has a card but neither a guardian nor a bus.
        Assert.Equal(before.Gaps.StudentsWithoutGuardian + 1, after.Gaps.StudentsWithoutGuardian);
        Assert.Equal(before.Gaps.StudentsWithoutTransport + 1, after.Gaps.StudentsWithoutTransport);
        Assert.Equal(before.Gaps.StudentsWithoutRfid, after.Gaps.StudentsWithoutRfid);
    }

    [Fact]
    public async Task GapAlerts_NameTheStudent_AndClearWhenFixed()
    {
        var admin = await _api.AdminClientAsync();
        int studentId, cardId;
        await using (var db = _api.NewDb())
        {
            // Earlier ids first: keep this student inside the first five named per alert kind.
            foreach (var s in db.Students.Where(s => s.Status == RecordStatus.Active)) s.Status = RecordStatus.Inactive;
            var student = new Student { FullName = Unique("Gap Kid "), DateOfBirth = new DateTime(2016, 1, 1) };
            db.Students.Add(student);
            await db.SaveChangesAsync();
            var card = new RfidCard { CardUid = Unique("GAP-"), StudentId = student.StudentId, AssignedAt = DateTime.UtcNow, Status = RfidCardStatus.Lost };
            db.RfidCards.Add(card);
            await db.SaveChangesAsync();
            (studentId, cardId) = (student.StudentId, card.RfidCardId);
        }

        var alerts = (await SummaryAsync(admin)).Alerts;
        Assert.Contains(alerts, a => a.Key == $"no-guardian-{studentId}" && a.Severity == AlertSeverity.Warning);
        Assert.Contains(alerts, a => a.Key == $"no-transport-{studentId}" && a.Severity == AlertSeverity.Info);
        var lost = Assert.Single(alerts, a => a.Key == $"card-{cardId}");
        Assert.Equal($"/Rfid/Details/{cardId}", lost.Link);

        await using (var db = _api.NewDb())
        {
            var guardian = new Guardian { FullName = Unique("Gap Parent "), Email = $"{Unique("gp")}@dash.test", PasswordHash = "x" };
            db.Guardians.Add(guardian);
            await db.SaveChangesAsync();
            db.StudentGuardians.Add(new StudentGuardian { StudentId = studentId, GuardianId = guardian.GuardianId, Relationship = "Father" });
            await db.SaveChangesAsync();
        }
        var bus = await (await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("GB"), 10, null, null), Json.Options)).Content.ReadFromJsonAsync<BusDto>(Json.Options);
        (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(studentId, bus!.BusId, null), Json.Options)).EnsureSuccessStatusCode();

        alerts = (await SummaryAsync(admin)).Alerts;
        Assert.DoesNotContain(alerts, a => a.Key == $"no-guardian-{studentId}" || a.Key == $"no-transport-{studentId}");
    }

    [Fact]
    public async Task LicenceAndBusAlerts_FollowExpiryAndStatus_AndBusLoadsCountRiders()
    {
        var admin = await _api.AdminClientAsync();
        var before = (await SummaryAsync(admin)).Admin!;
        int expired, expiring, valid, inactiveExpired, maintenance, outOfService, inService;
        await using (var db = _api.NewDb())
        {
            var d1 = new Driver { FullName = Unique("Expired "), LicenseExpiresOn = new DateOnly(2026, 11, 1) };
            var d2 = new Driver { FullName = Unique("Expiring "), LicenseExpiresOn = new DateOnly(2026, 11, 30) };
            var d3 = new Driver { FullName = Unique("Valid "), LicenseExpiresOn = new DateOnly(2028, 1, 1) };
            var d4 = new Driver { FullName = Unique("Retired "), LicenseExpiresOn = new DateOnly(2025, 1, 1), Status = RecordStatus.Inactive };
            var b1 = new Bus { BusNumber = Unique("M"), Capacity = 20, Status = BusStatus.UnderMaintenance };
            var b2 = new Bus { BusNumber = Unique("O"), Capacity = 20, Status = BusStatus.OutOfService };
            var b3 = new Bus { BusNumber = Unique("A"), Capacity = 2 };
            db.Drivers.AddRange(d1, d2, d3, d4);
            db.Buses.AddRange(b1, b2, b3);
            await db.SaveChangesAsync();
            (expired, expiring, valid, inactiveExpired) = (d1.DriverId, d2.DriverId, d3.DriverId, d4.DriverId);
            (maintenance, outOfService, inService) = (b1.BusId, b2.BusId, b3.BusId);
        }

        // One rider on the bus that then goes in for maintenance.
        await using (var db = _api.NewDb())
        {
            var s = new Student { FullName = Unique("Rider "), DateOfBirth = new DateTime(2016, 1, 1) };
            db.Students.Add(s);
            await db.SaveChangesAsync();
            db.StudentBusAssignments.Add(new StudentBusAssignment { StudentId = s.StudentId, BusId = maintenance, AssignedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var summary = await SummaryAsync(admin);
        var after = summary.Admin!;
        Assert.Equal(before.Gaps.LicencesExpired + 1, after.Gaps.LicencesExpired);
        Assert.Equal(before.Gaps.LicencesExpiringSoon + 1, after.Gaps.LicencesExpiringSoon);
        Assert.Equal(before.Fleet.Drivers + 4, after.Fleet.Drivers);
        Assert.Equal(before.Fleet.ActiveDrivers + 3, after.Fleet.ActiveDrivers);
        Assert.Equal((before.Fleet.BusesUnderMaintenance + 1, before.Fleet.BusesOutOfService + 1, before.Fleet.BusesActive + 1),
            (after.Fleet.BusesUnderMaintenance, after.Fleet.BusesOutOfService, after.Fleet.BusesActive));
        Assert.Equal(1, after.Fleet.BusLoads.Single(b => b.BusId == maintenance).Students);
        Assert.Equal((0, 2), (after.Fleet.BusLoads.Single(b => b.BusId == inService).Students, after.Fleet.BusLoads.Single(b => b.BusId == inService).Capacity));

        var alerts = summary.Alerts;
        Assert.Equal(AlertSeverity.Critical, alerts.Single(a => a.Key == $"licence-{expired}").Severity);
        Assert.Equal(AlertSeverity.Warning, alerts.Single(a => a.Key == $"licence-{expiring}").Severity);
        Assert.DoesNotContain(alerts, a => a.Key == $"licence-{valid}" || a.Key == $"licence-{inactiveExpired}");
        var maint = alerts.Single(a => a.Key == $"bus-{maintenance}");
        Assert.Equal(AlertSeverity.Warning, maint.Severity);
        Assert.Contains("1 student is still assigned", maint.Detail);
        Assert.Equal(AlertSeverity.Info, alerts.Single(a => a.Key == $"bus-{outOfService}").Severity);
        Assert.DoesNotContain(alerts, a => a.Key == $"bus-{inService}");

        // Alerts stay ordered by severity.
        Assert.Equal(alerts.Select(a => a.Severity).OrderByDescending(s => s), alerts.Select(a => a.Severity));
    }

    [Fact]
    public async Task Summary_RequiresAnAdmin()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/dashboard/summary")).StatusCode);
        var guardian = _api.GuardianClient((await _api.NewChildAsync(withFace: false)).GuardianId);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/dashboard/summary")).StatusCode);
    }
}
