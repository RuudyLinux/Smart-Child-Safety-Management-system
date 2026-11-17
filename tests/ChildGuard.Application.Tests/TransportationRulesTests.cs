using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Import;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Application.Validators;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using K = ChildGuard.Application.Import.ImportSchema.Keys;

namespace ChildGuard.Application.Tests;

/// <summary>
/// Transport business rules: bus capacity, one active assignment per student, out-of-service
/// buses, deactivated drivers and routes, the route-override switch, assignment history, audit,
/// and the transport snapshot on journey events.
/// </summary>
public class TransportationRulesTests
{
    private const int Admin = 9;

    private static (TransportationService Service, FakeTransportationRepository Repo) Create(bool allowRouteOverride = false)
    {
        var repo = new FakeTransportationRepository();
        var (clock, _) = TestClock.Create();
        return (new TransportationService(repo, clock, new TransportOptions { AllowRouteOverride = allowRouteOverride }), repo);
    }

    private static Task<Result> Assign(TransportationService s, int studentId, int busId, int? routeId = null) =>
        s.AddAssignmentAsync(new CreateStudentBusAssignmentRequest(studentId, busId, routeId), default, Admin);

    // ---------------------------------------------------------------- capacity

    [Fact]
    public async Task FullBus_RefusesAnotherStudent()
    {
        var (s, repo) = Create();
        var bus = repo.AddBus("B1", capacity: 1);
        Assert.True((await Assign(s, repo.AddStudent().StudentId, bus.BusId)).Succeeded);

        var refused = await Assign(s, repo.AddStudent().StudentId, bus.BusId);

        Assert.Equal(TransportationService.BusFull("B1", 1), refused.Error);
        Assert.Single(repo.Assignments);
    }

    [Fact]
    public async Task Capacity_CannotDropBelowCurrentStudents()
    {
        var (s, repo) = Create();
        var bus = repo.AddBus("B1", capacity: 5);
        repo.Seat(repo.AddStudent().StudentId, bus.BusId);
        repo.Seat(repo.AddStudent().StudentId, bus.BusId);

        var refused = await s.UpdateBusAsync(bus.BusId, new UpdateBusRequest("B1", 1, null, null), default, Admin);
        Assert.Equal(TransportationService.CapacityBelowOccupancy(1, 2), refused.Error);
        Assert.Equal(5, bus.Capacity);

        Assert.True((await s.UpdateBusAsync(bus.BusId, new UpdateBusRequest("B1", 2, null, null), default, Admin)).Succeeded);
        Assert.Equal(2, bus.Capacity);
        Assert.Contains(repo.Locks, l => l.Buses.Contains(bus.BusId)); // checked under the bus lock
    }

    [Fact]
    public async Task Occupancy_IsRecalculatedOnEveryRead()
    {
        var (s, repo) = Create();
        var bus = repo.AddBus("B1", capacity: 3);
        var kid = repo.AddStudent();
        await Assign(s, kid.StudentId, bus.BusId);
        Assert.Equal(1, (await s.GetBusesAsync()).Single().ActiveStudentCount);

        await s.SetAssignmentStatusAsync(repo.Assignments.Single().Id, RecordStatus.Inactive, default, Admin);
        Assert.Equal(0, (await s.GetBusesAsync()).Single().ActiveStudentCount);
    }

    // ---------------------------------------------------------------- assignments

    [Fact]
    public async Task ChangingBus_EndsTheOldAssignmentAndCreatesANewOne()
    {
        var (s, repo) = Create();
        var route1 = repo.AddRoute("North");
        var route2 = repo.AddRoute("South");
        var b1 = repo.AddBus("B1", 10, route1.RouteId);
        var b2 = repo.AddBus("B2", 10, route2.RouteId);
        var kid = repo.AddStudent();

        await Assign(s, kid.StudentId, b1.BusId);
        var moved = await Assign(s, kid.StudentId, b2.BusId);

        Assert.True(moved.Succeeded);
        var old = repo.Assignments.Single(a => a.BusId == b1.BusId);
        var current = repo.Assignments.Single(a => a.BusId == b2.BusId);
        Assert.Equal((RecordStatus.Inactive, true), (old.Status, old.EndedAt.HasValue));
        Assert.Equal((RecordStatus.Active, (int?)route2.RouteId, (DateTime?)null), (current.Status, current.RouteId, current.EndedAt));
        Assert.Equal(new[] { "Assigned", "Reassigned" }, repo.Audit.Where(a => a.EntityType == "Assignment").Select(a => a.Action));
        Assert.All(repo.Audit, a => Assert.Equal(Admin, a.AdminId));
    }

    [Fact]
    public async Task MovingToAFullBus_LeavesTheCurrentAssignmentUntouched()
    {
        var (s, repo) = Create();
        var b1 = repo.AddBus("B1", 10);
        var full = repo.AddBus("FULL", 1);
        repo.Seat(repo.AddStudent().StudentId, full.BusId);
        var kid = repo.AddStudent();
        var current = repo.Seat(kid.StudentId, b1.BusId);

        var refused = await Assign(s, kid.StudentId, full.BusId);

        Assert.False(refused.Succeeded);
        Assert.Equal((RecordStatus.Active, (DateTime?)null), (current.Status, current.EndedAt));
    }

    [Fact]
    public async Task SameBusAgain_IsRefused_AndAnEndedAssignmentIsNeverReactivated()
    {
        var (s, repo) = Create();
        var bus = repo.AddBus("B1", 10);
        var kid = repo.AddStudent();
        await Assign(s, kid.StudentId, bus.BusId);

        Assert.Equal(TransportationService.AlreadyOnThisBus, (await Assign(s, kid.StudentId, bus.BusId)).Error);

        var id = repo.Assignments.Single().Id;
        Assert.True((await s.SetAssignmentStatusAsync(id, RecordStatus.Inactive, default, Admin)).Succeeded);
        Assert.NotNull(repo.Assignments.Single().EndedAt);
        Assert.Equal(TransportationService.EndedAssignmentIsHistory, (await s.SetAssignmentStatusAsync(id, RecordStatus.Active, default, Admin)).Error);
    }

    [Fact]
    public async Task OutOfServiceBus_TakesNoNewStudents_ButKeepsItsCurrentOnes()
    {
        var (s, repo) = Create();
        var bus = repo.AddBus("B1", 10);
        var seated = repo.Seat(repo.AddStudent().StudentId, bus.BusId);

        await s.SetBusStatusAsync(bus.BusId, RecordStatus.Inactive, default, Admin);

        Assert.Equal(TransportationService.BusOutOfService, (await Assign(s, repo.AddStudent().StudentId, bus.BusId)).Error);
        Assert.Equal(RecordStatus.Active, seated.Status);
    }

    // ---------------------------------------------------------------- routes

    [Fact]
    public async Task Route_DefaultsToTheBusRoute_AndOverridesFollowTheSchoolSetting()
    {
        var (strict, repo) = Create(allowRouteOverride: false);
        var north = repo.AddRoute("North");
        var south = repo.AddRoute("South");
        var closed = repo.AddRoute("Closed", RecordStatus.Inactive);
        var bus = repo.AddBus("B1", 10, north.RouteId);

        Assert.Equal(TransportationService.RouteOverrideNotAllowed, (await Assign(strict, repo.AddStudent().StudentId, bus.BusId, south.RouteId)).Error);
        var kid = repo.AddStudent();
        Assert.True((await Assign(strict, kid.StudentId, bus.BusId)).Succeeded);
        Assert.Equal(north.RouteId, repo.Assignments.Single().RouteId);

        var (lenient, repo2) = Create(allowRouteOverride: true);
        repo2.Routes.AddRange(new[] { north, south, closed });
        repo2.Buses.Add(bus);
        var other = repo2.AddStudent();
        Assert.Equal(TransportationService.RouteInactive, (await Assign(lenient, other.StudentId, bus.BusId, closed.RouteId)).Error);
        Assert.Equal(TransportationService.RouteNotFound, (await Assign(lenient, other.StudentId, bus.BusId, 9999)).Error);
        Assert.True((await Assign(lenient, other.StudentId, bus.BusId, south.RouteId)).Succeeded);
        Assert.Equal(south.RouteId, repo2.Assignments.Single().RouteId);
    }

    // ---------------------------------------------------------------- drivers and routes on buses

    [Fact]
    public async Task DeactivatedDriverAndRoute_CannotBeNewlyAssigned_ButStayWhereTheyAre()
    {
        var (s, repo) = Create();
        var driver = repo.AddDriver("Ramesh");
        var route = repo.AddRoute("North");
        var bus = repo.AddBus("B1", 10, route.RouteId, driverId: driver.DriverId);

        await s.SetDriverStatusAsync(driver.DriverId, RecordStatus.Inactive, default, Admin);
        await s.SetRouteStatusAsync(route.RouteId, RecordStatus.Inactive, default, Admin);

        // Existing relationships are intact, and keeping them through an edit is allowed.
        Assert.Equal((driver.DriverId, route.RouteId), (bus.DriverId!.Value, bus.RouteId!.Value));
        Assert.True((await s.UpdateBusAsync(bus.BusId, new UpdateBusRequest("B1", 12, driver.DriverId, route.RouteId), default, Admin)).Succeeded);

        // A new bus may not take them.
        Assert.Equal(TransportationService.DriverInactive,
            (await s.AddBusAsync(new CreateBusRequest("B2", 10, driver.DriverId, null), default, Admin)).Error);
        Assert.Equal(TransportationService.RouteInactive,
            (await s.AddBusAsync(new CreateBusRequest("B3", 10, null, route.RouteId), default, Admin)).Error);
        // Nor may another existing bus switch to them.
        var other = repo.AddBus("B4", 10);
        Assert.Equal(TransportationService.DriverInactive,
            (await s.UpdateBusAsync(other.BusId, new UpdateBusRequest("B4", 10, driver.DriverId, null), default, Admin)).Error);
    }

    [Fact]
    public async Task DuplicateBusNumbersAndRouteNames_AreRefused()
    {
        var (s, repo) = Create();
        repo.AddBus("BUS-1", 10);
        repo.AddRoute("North Loop");

        Assert.Equal(TransportationService.DuplicateBusNumber, (await s.AddBusAsync(new CreateBusRequest(" bus-1 ", 10, null, null))).Error);
        Assert.Equal(TransportationService.DuplicateRouteName, (await s.AddRouteAsync(new CreateRouteRequest("north loop", null))).Error);
    }

    [Fact]
    public async Task EveryChangeIsAudited()
    {
        var (s, repo) = Create();
        var driver = await s.AddDriverAsync(new CreateDriverRequest("Ramesh", null, "LIC-1"), default, Admin);
        var route = (await s.AddRouteAsync(new CreateRouteRequest("North", null), default, Admin)).Value!;
        var bus = (await s.AddBusAsync(new CreateBusRequest("B1", 10, driver.DriverId, route.RouteId), default, Admin)).Value!;
        await s.SetBusStatusAsync(bus.BusId, RecordStatus.Inactive, default, Admin);

        Assert.Equal(
            new[] { ("Driver", "Created"), ("Route", "Created"), ("Bus", "Created"), ("Bus", "TakenOutOfService") },
            repo.Audit.Select(a => (a.EntityType, a.Action)));
        var listed = await s.GetAuditAsync(10);
        Assert.Equal("TakenOutOfService", listed[0].Action); // newest first
    }

    // ---------------------------------------------------------------- journey snapshot

    [Fact]
    public async Task JourneySnapshot_StampsTheAssignmentAsItIsNow()
    {
        var journey = new FakeJourneyRepository();
        journey.ActiveAssignments[5] = new StudentBusAssignment { Id = 77, StudentId = 5, BusId = 3, RouteId = 4 };
        var e = new JourneyEvent { StudentId = 5, EventType = JourneyEventType.BusBoarding };

        await JourneyTransportSnapshot.StampAsync(journey, e, default);

        Assert.Equal((77, 3, 4), (e.StudentBusAssignmentId!.Value, e.BusId!.Value, e.RouteId!.Value));

        var unassigned = new JourneyEvent { StudentId = 6 };
        await JourneyTransportSnapshot.StampAsync(journey, unassigned, default);
        Assert.Null(unassigned.BusId);
    }

    // ---------------------------------------------------------------- bulk import

    [Fact]
    public async Task StudentImport_ChecksBusServiceAndSeats()
    {
        var repo = new FakeBulkImportRepository();
        repo.Buses.Add(new BusImportKey(1, "B1", 7, Capacity: 3, ActiveStudents: 1));
        repo.Buses.Add(new BusImportKey(2, "OFF", null, Capacity: 30, Status: BusStatus.Inactive));
        var handler = new StudentImportHandler(repo, new CreateStudentRequestValidator());

        ImportRowDto Kid(int n, string bus, string dob = "2016-01-01") =>
            new(n, new() { [K.FullName] = $"Kid {n}", [K.DateOfBirth] = dob, [K.BusNumber] = bus });

        var plans = await handler.PlanAsync(new[]
        {
            Kid(2, "B1"),
            Kid(3, "B1", dob: "2099-01-01"), // invalid for another reason: must not take a seat
            Kid(4, "b1"),
            Kid(5, "B1"),                     // 1 seated + 2 earlier rows = full
            Kid(6, "OFF"),
        }, default);

        Assert.True(plans[0].IsValid);
        Assert.False(plans[1].IsValid);
        Assert.True(plans[2].IsValid);
        Assert.Contains(plans[3].Errors, e => e.Column == K.BusNumber && e.Message.Contains("full"));
        Assert.Contains(plans[4].Errors, e => e.Column == K.BusNumber && e.Message.Contains("out of service"));

        var set = new ImportWriteSet();
        handler.Stage(plans.Where(p => p.IsValid).ToList(), set, DateTime.UtcNow);
        Assert.All(set.Students, st => Assert.Equal((1, (int?)7), (st.BusAssignments.Single().BusId, st.BusAssignments.Single().RouteId)));
    }

    [Fact]
    public async Task BusImport_RefusesDeactivatedDriversAndRoutes()
    {
        var repo = new FakeBulkImportRepository();
        repo.Drivers.Add(new DriverImportKey(1, "Gone", null, "LIC-OFF", RecordStatus.Inactive));
        repo.Routes.Add(new RouteImportKey(1, "Closed", RecordStatus.Inactive));
        var handler = new BusImportHandler(repo, new CreateBusRequestValidator());

        var plan = (await handler.PlanAsync(new[]
        {
            new ImportRowDto(2, new() { [K.BusNumber] = "B9", [K.Capacity] = "20", [K.DriverLicenseNumber] = "LIC-OFF", [K.RouteName] = "Closed" })
        }, default)).Single();

        Assert.Contains(plan.Errors, e => e.Column == K.DriverLicenseNumber && e.Message.Contains("deactivated"));
        Assert.Contains(plan.Errors, e => e.Column == K.RouteName && e.Message.Contains("deactivated"));
    }
}
