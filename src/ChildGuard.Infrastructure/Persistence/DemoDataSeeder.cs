using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Persistence;

/// <summary>What the demo seeder created, for the reset confirmation and the audit trail.</summary>
public sealed record DemoSeedSummary(
    int Students, int Guardians, int Drivers, int Buses, int Routes, int Stops, int RfidCards,
    int TransportAssignments, int JourneyEvents, int AttendanceRecords, int Notifications, int Photos);

/// <summary>
/// The demo school: five bus routes and their timed stops (school_bus_routes_demo.json), a bus and a driver per
/// route plus a reserve driver, and 10 families with 15 students (demo_people.json) whose children ride the route
/// through their home stop (one family lives next to the school and has no transport). Every student gets an admission number, guardians with pickup rights, an RFID card (one is left
/// pending and one card was reported lost and replaced, so those screens have something to show), a transport
/// assignment with pickup and drop-off stops, a generated initials avatar, and the last five school days of
/// journey and attendance history. Today is left empty for live scanning.
///
/// The data files are embedded in this assembly. Writes only rows; it never deletes anything, so it must run on an
/// empty database (the database reset clears it first).
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoGuardianPassword = "Guardian@123";
    private const int HistoryDays = 5;

    /// <summary>Development startup option (SeedDemoData): seeds once, only while there are no students.</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChildGuardDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<object>>();
        if (await db.Students.AnyAsync())
        {
            logger.LogInformation("Demo data skipped — Students table is not empty.");
            return;
        }
        var summary = await SeedIntoAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasherService>(),
            scope.ServiceProvider.GetRequiredService<SchoolClock>(), CancellationToken.None);
        logger.LogInformation("Demo data seeded: {Summary}", summary);
    }

    public static async Task<DemoSeedSummary> SeedIntoAsync(ChildGuardDbContext db, IPasswordHasherService hasher, SchoolClock clock, CancellationToken ct)
    {
        var routesFile = ReadJson("school_bus_routes_demo.json");
        var people = ReadJson("demo_people.json");
        var rng = new Random(2026);
        var now = clock.UtcNow;
        var today = clock.Today;
        var termStart = clock.StartOfDayUtc(today.AddDays(-120)).AddHours(4);
        var photos = 0;

        // ---------------------------------------------------------------- routes and stops
        var routes = new Dictionary<string, Route>();
        var routeBus = new Dictionary<string, string>();
        foreach (var r in routesFile.GetProperty("routes").EnumerateArray())
        {
            var code = r.GetProperty("routeId").GetString()!;
            var route = new Route
            {
                RouteName = r.GetProperty("routeName").GetString()!,
                Description = $"Route {code}. Demo route: verify stops, road alignment and timings before real use.",
                StartPoint = r.GetProperty("startPoint").GetString(),
                Destination = r.GetProperty("destination").GetString(),
                CreatedAt = termStart
            };
            foreach (var s in r.GetProperty("stops").EnumerateArray())
                route.Stops.Add(new RouteStop
                {
                    Sequence = s.GetProperty("stopOrder").GetInt32(),
                    Name = s.GetProperty("stopName").GetString()!,
                    Address = s.GetProperty("addressOrLandmark").GetString(),
                    PickupTime = Time(s.GetProperty("pickupTime").GetString()),
                    DropTime = Time(s.GetProperty("dropOffTime").GetString()),
                    CreatedAt = termStart
                });
            routes[code] = route;
            routeBus[r.GetProperty("busNumber").GetString()!] = code;
            db.Routes.Add(route);
        }

        // ---------------------------------------------------------------- drivers and buses
        var drivers = new Dictionary<string, Driver>();
        foreach (var d in people.GetProperty("drivers").EnumerateArray())
        {
            var driver = new Driver
            {
                FullName = Str(d, "name")!, EmployeeCode = Str(d, "code"), Phone = Str(d, "phone"), Email = Str(d, "email"),
                LicenseNumber = Str(d, "licenseNumber"), LicenseCategory = Str(d, "category"),
                LicenseExpiresOn = today.AddDays(d.GetProperty("licenseExpiresInDays").GetInt32()),
                JoinedOn = DateOnly.Parse(Str(d, "joinedOn")!, CultureInfo.InvariantCulture),
                EmergencyContactName = Str(d, "emergencyName"), EmergencyContactPhone = Str(d, "emergencyPhone"),
                Address = Address(d.GetProperty("address")), CreatedAt = termStart
            };
            if (Avatar(driver.FullName) is { } img) { driver.Photo = Photo<DriverPhoto>(img, now); photos++; }
            db.Drivers.Add(driver);
            if (Str(d, "bus") is { } bus) drivers[bus] = driver;
        }

        var buses = new Dictionary<string, Bus>();
        foreach (var b in people.GetProperty("buses").EnumerateArray())
        {
            var number = Str(b, "number")!;
            var bus = new Bus
            {
                BusNumber = number, RegistrationNumber = Str(b, "registration"), Manufacturer = Str(b, "manufacturer"),
                Model = Str(b, "model"), Capacity = b.GetProperty("capacity").GetInt32(),
                Driver = drivers.GetValueOrDefault(number), Route = routeBus.TryGetValue(number, out var rc) ? routes[rc] : null,
                CreatedAt = termStart
            };
            buses[number] = bus;
            db.Buses.Add(bus);
        }
        var busByRoute = buses.Values.Where(b => b.Route is not null).ToDictionary(b => routes.First(r => r.Value == b.Route).Key);
        await db.SaveChangesAsync(ct);

        // ---------------------------------------------------------------- families
        var guardianHash = hasher.Hash(DemoGuardianPassword);
        var studentPlans = new List<(Student Student, List<Guardian> Guardians, string? Route, string? Stop, JsonElement Child)>();
        var guardianCount = 0;
        foreach (var f in people.GetProperty("families").EnumerateArray())
        {
            var home = Address(f.GetProperty("address"));
            var family = new List<(Guardian G, string Relationship)>();
            foreach (var g in f.GetProperty("guardians").EnumerateArray())
            {
                var name = Str(g, "name")!;
                var guardian = new Guardian
                {
                    FullName = name, Email = $"{Slug(name).Replace('-', '.')}@example.com", Phone = Str(g, "phone"),
                    Address = Address(f.GetProperty("address")), PasswordHash = guardianHash, PasswordResetRequired = true,
                    CreatedAt = termStart, UpdatedAt = termStart
                };
                if (Avatar(name) is { } img) { guardian.Photo = Photo<GuardianPhoto>(img, now); photos++; }
                db.Guardians.Add(guardian);
                family.Add((guardian, Str(g, "relationship")!));
                guardianCount++;
            }
            foreach (var c in f.GetProperty("children").EnumerateArray())
            {
                var name = Str(c, "name")!;
                var student = new Student
                {
                    FullName = name, Gender = Enum.Parse<Gender>(Str(c, "gender")!),
                    // A calendar date, not an instant (see ChildGuardDbContext.MarkInstantsAsUtc).
                    DateOfBirth = DateTime.SpecifyKind(DateTime.Parse(Str(c, "dob")!, CultureInfo.InvariantCulture), DateTimeKind.Unspecified),
                    Grade = Str(c, "grade"), Section = Str(c, "section"), Address = home, CreatedAt = termStart, UpdatedAt = termStart
                };
                if (Avatar(name) is { } img) { student.Photo = Photo<StudentPhoto>(img, now); photos++; }
                foreach (var (g, rel) in family)
                    student.StudentGuardians.Add(new StudentGuardian { Guardian = g, Relationship = rel, IsAuthorizedForPickup = true, CreatedAt = termStart });
                db.Students.Add(student);
                studentPlans.Add((student, family.Select(x => x.G).ToList(), Str(f, "route"), Str(f, "stop"), c));
                home = Address(f.GetProperty("address")); // each owned address instance belongs to one owner
            }
        }
        await db.SaveChangesAsync(ct);

        // Admission numbers come from the new ids, the same rule as an enrollment.
        foreach (var p in studentPlans) p.Student.AdmissionNumber = AdmissionNumbers.For(p.Student.StudentId, today);

        // ---------------------------------------------------------------- RFID cards
        var usedUids = new HashSet<string>();
        string NewUid() { string u; do u = "04" + rng.NextInt64(0x10000000L, 0xFFFFFFFFL).ToString("X8") + rng.Next(0x10, 0xFF).ToString("X2"); while (!usedUids.Add(u)); return u; }
        var cards = new Dictionary<Student, RfidCard>();
        var cardCount = 0;
        foreach (var p in studentPlans)
        {
            if (Bool(p.Child, "noCard")) continue;
            if (Bool(p.Child, "lostCard"))
            {
                // Lost three weeks ago and replaced: the card stays on record with its history.
                var lostAt = now.AddDays(-21);
                var lost = new RfidCard { CardUid = NewUid(), Status = RfidCardStatus.Lost, CreatedAt = termStart };
                lost.Assignments.Add(new RfidCardAssignment
                {
                    Student = p.Student, AssignedAt = termStart, RecordedAt = termStart,
                    EndedAt = lostAt, EndReason = "Card reported lost by the guardian; replaced."
                });
                db.RfidCards.Add(lost);
                cardCount++;
                var replacement = AssignedCard(p.Student, NewUid(), lostAt);
                db.RfidCards.Add(replacement);
                cards[p.Student] = replacement;
                cardCount++;
                continue;
            }
            var card = AssignedCard(p.Student, NewUid(), termStart);
            db.RfidCards.Add(card);
            cards[p.Student] = card;
            cardCount++;
        }
        for (var i = 0; i < 4; i++) { db.RfidCards.Add(new RfidCard { CardUid = NewUid(), CreatedAt = termStart }); cardCount++; }
        db.RfidCards.Add(new RfidCard { CardUid = NewUid(), Status = RfidCardStatus.Damaged, CreatedAt = termStart });
        cardCount++;

        // ---------------------------------------------------------------- transport
        var rides = new Dictionary<Student, (StudentBusAssignment A, Bus Bus, Route Route, RouteStop Stop, RouteStop School)>();
        foreach (var p in studentPlans.Where(p => p.Route is not null))
        {
            var route = routes[p.Route!];
            var bus = busByRoute[p.Route!];
            var stop = route.Stops.Single(s => s.Name == p.Stop);
            var school = route.Stops.OrderBy(s => s.Sequence).Last();
            var a = new StudentBusAssignment
            {
                Student = p.Student, Bus = bus, Route = route, PickupStop = stop, DropStop = stop, AssignedAt = termStart
            };
            db.StudentBusAssignments.Add(a);
            rides[p.Student] = (a, bus, route, stop, school);
        }
        await db.SaveChangesAsync(ct);

        // ---------------------------------------------------------------- the last school days
        var gate = await db.RfidDevices.OrderBy(d => d.RfidDeviceId).Select(d => (int?)d.RfidDeviceId).FirstOrDefaultAsync(ct);
        var days = new List<DateOnly>();
        for (var d = today.AddDays(-1); days.Count < HistoryDays; d = d.AddDays(-1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days.Add(d);
        days.Reverse();

        int events = 0, attendance = 0, notifications = 0;
        foreach (var day in days)
        {
            var dayStart = clock.StartOfDayUtc(day);
            DateTime At(TimeOnly t, int jitterMinutes) => dayStart.Add(t.ToTimeSpan()).AddMinutes(jitterMinutes);
            var lastDay = day == days[^1];

            foreach (var p in studentPlans)
            {
                if (!cards.TryGetValue(p.Student, out var card)) continue; // no card: never scanned
                if (rng.NextDouble() < 0.06) continue;                    // absent that day

                var journey = new List<(JourneyEventType Type, DateTime At, bool OnBus)>();
                if (rides.TryGetValue(p.Student, out var ride))
                {
                    journey.Add((JourneyEventType.BusBoarding, At(ride.Stop.PickupTime!.Value, rng.Next(-3, 4)), true));
                    journey.Add((JourneyEventType.SchoolArrival, At(ride.School.PickupTime!.Value, rng.Next(2, 9)), false));
                    journey.Add((JourneyEventType.SchoolDeparture, At(ride.School.DropTime!.Value, -rng.Next(2, 8)), false));
                    journey.Add((JourneyEventType.BusDropoff, At(ride.Stop.DropTime!.Value, rng.Next(0, 6)), true));
                }
                else
                {
                    // Walkers and parents' drop-offs; now and then one arrives after the 08:30 late mark.
                    var late = rng.NextDouble() < 0.15;
                    journey.Add((JourneyEventType.SchoolArrival, At(new TimeOnly(late ? 8 : 7, late ? 35 : 50), rng.Next(0, 9)), false));
                    journey.Add((JourneyEventType.SchoolDeparture, At(new TimeOnly(14, 10), rng.Next(0, 12)), false));
                }

                foreach (var (type, at, onBus) in journey)
                {
                    db.JourneyEvents.Add(new JourneyEvent
                    {
                        StudentId = p.Student.StudentId, RfidCardId = card.RfidCardId, EventType = type, Timestamp = at,
                        Source = EventSource.Rfid, RfidDeviceId = onBus ? null : gate, CreatedAt = at,
                        BusId = onBus ? ride.Bus.BusId : null, RouteId = onBus ? ride.Route.RouteId : null,
                        StudentBusAssignmentId = onBus ? ride.A.Id : null
                    });
                    events++;
                    if (type is JourneyEventType.SchoolArrival or JourneyEventType.SchoolDeparture)
                    {
                        var (date, time) = clock.ToSchoolDateTime(at);
                        db.AttendanceRecords.Add(new Attendance
                        {
                            StudentId = p.Student.StudentId, RfidCardId = card.RfidCardId, RecordedAtUtc = at, Date = date, Time = time,
                            EventType = type == JourneyEventType.SchoolArrival ? AttendanceEventType.SchoolArrival : AttendanceEventType.SchoolDeparture,
                            CreatedAt = at
                        });
                        attendance++;
                    }
                    if (!lastDay) continue;
                    var label = type switch
                    {
                        JourneyEventType.BusBoarding => $"boarded bus {ride.Bus.BusNumber} at {ride.Stop.Name}",
                        JourneyEventType.SchoolArrival => "arrived at school",
                        JourneyEventType.SchoolDeparture => "left school",
                        _ => $"was dropped off at {ride.Stop.Name}"
                    };
                    var local = clock.ToSchoolDateTime(at).Time.ToString("h:mm tt", CultureInfo.InvariantCulture);
                    foreach (var g in p.Guardians)
                    {
                        db.Notifications.Add(new Notification
                        {
                            Guardian = g, StudentId = p.Student.StudentId, Type = NotificationType.Journey,
                            Title = $"{p.Student.FullName.Split(' ')[0]} {label}",
                            Message = $"{p.Student.FullName} {label} at {local}.", SentAt = at, IsRead = true
                        });
                        notifications++;
                    }
                }
            }
        }
        await db.SaveChangesAsync(ct);

        return new DemoSeedSummary(studentPlans.Count, guardianCount, people.GetProperty("drivers").GetArrayLength(),
            buses.Count, routes.Count, routes.Values.Sum(r => r.Stops.Count), cardCount, rides.Count, events, attendance, notifications, photos);
    }

    private static RfidCard AssignedCard(Student student, string uid, DateTime at)
    {
        var card = new RfidCard { CardUid = uid, Student = student, AssignedAt = at, CreatedAt = at };
        card.Assignments.Add(new RfidCardAssignment { Student = student, AssignedAt = at, RecordedAt = at });
        return card;
    }

    private static T Photo<T>(byte[] png, DateTime at) where T : ProfilePhoto, new() =>
        new() { Data = png, ContentType = "image/png", ByteSize = png.Length, UpdatedAt = at };

    private static PostalAddress Address(JsonElement a) => new()
    {
        Line1 = Str(a, "line1"), Locality = Str(a, "locality"), Landmark = Str(a, "landmark"), City = Str(a, "city"),
        District = Str(a, "district"), State = Str(a, "state"), PostalCode = Str(a, "postalCode"), Country = "India"
    };

    private static TimeOnly? Time(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : TimeOnly.ParseExact(s.Trim(), "hh:mm tt", CultureInfo.InvariantCulture);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Slug(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private static readonly Assembly Self = typeof(DemoDataSeeder).Assembly;

    private static Stream? Resource(string relative)
    {
        var name = Self.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == "DemoData/" + relative);
        return name is null ? null : Self.GetManifestResourceStream(name);
    }

    private static JsonElement ReadJson(string file)
    {
        using var stream = Resource(file) ?? throw new InvalidOperationException($"Demo data file {file} is missing from the build.");
        return JsonDocument.Parse(stream).RootElement.Clone();
    }

    private static byte[]? Avatar(string name)
    {
        using var stream = Resource($"avatars/{Slug(name)}.png");
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
