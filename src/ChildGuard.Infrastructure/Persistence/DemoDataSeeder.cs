using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Persistence;

/// <summary>
/// Populates realistic demo/presentation data — students, guardians, buses, routes,
/// drivers, RFID cards, a day of attendance/journey history — so the Admin Web,
/// Flutter app and demo verification page have something to show without manual
/// data entry. Idempotent: skipped once any Student row exists. Never runs unless
/// explicitly requested (see Program.cs) — separate from the required-account
/// DbSeeder, and safe to run only against a dev database.
/// </summary>
public static class DemoDataSeeder
{
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

        var hasher = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IPasswordHasherService>();

        // --- Guardians ---
        var priya = new Guardian { FullName = "Priya Patel", Email = "priya.patel@demo.childguard.local", Phone = "9820011111", PasswordHash = hasher.Hash("Guardian@123") };
        var amit = new Guardian { FullName = "Amit Shah", Email = "amit.shah@demo.childguard.local", Phone = "9820022222", PasswordHash = hasher.Hash("Guardian@123") };
        var sunita = new Guardian { FullName = "Sunita Verma", Email = "sunita.verma@demo.childguard.local", Phone = "9820033333", PasswordHash = hasher.Hash("Guardian@123") };
        db.Guardians.AddRange(priya, amit, sunita);

        // --- Drivers / Routes / Buses ---
        var driver1 = new Driver { FullName = "Ramesh Kumar", Phone = "9811100001", LicenseNumber = "MH-DL-000111" };
        var driver2 = new Driver { FullName = "Suresh Yadav", Phone = "9811100002", LicenseNumber = "MH-DL-000222" };
        db.Drivers.AddRange(driver1, driver2);

        var routeA = new Route { RouteName = "Route A - North Zone", Description = "Covers North residential zone stops." };
        var routeB = new Route { RouteName = "Route B - South Zone", Description = "Covers South residential zone stops." };
        db.Routes.AddRange(routeA, routeB);

        var bus1 = new Bus { BusNumber = "BUS-101", Capacity = 40, Driver = driver1, Route = routeA };
        var bus2 = new Bus { BusNumber = "BUS-102", Capacity = 35, Driver = driver2, Route = routeB };
        db.Buses.AddRange(bus1, bus2);

        // --- Students ---
        var rahul = new Student { FullName = "Rahul Patel", DateOfBirth = new DateTime(2015, 4, 10), Grade = "5A" };
        var isha = new Student { FullName = "Isha Shah", DateOfBirth = new DateTime(2016, 7, 22), Grade = "4B" };
        var kabir = new Student { FullName = "Kabir Verma", DateOfBirth = new DateTime(2014, 11, 3), Grade = "6C" };
        db.Students.AddRange(rahul, isha, kabir);

        // --- RFID devices/cards ---
        if (!await db.RfidDevices.AnyAsync())
            db.RfidDevices.Add(new RfidDevice { DeviceLabel = "RFID-001", ConnectionStatus = RfidConnectionStatus.Disconnected });

        var cardRahul = new RfidCard { CardUid = "04A1B2C3A72F", Student = rahul, AssignedAt = DateTime.UtcNow };
        var cardIsha = new RfidCard { CardUid = "04B2C3D4B831", Student = isha, AssignedAt = DateTime.UtcNow };
        var cardKabir = new RfidCard { CardUid = "04C3D4E5C942", Student = kabir, AssignedAt = DateTime.UtcNow };
        db.RfidCards.AddRange(cardRahul, cardIsha, cardKabir);

        // --- Guardian <-> Student links (pickup authorization) ---
        db.StudentGuardians.AddRange(
            new StudentGuardian { Student = rahul, Guardian = priya, Relationship = "Mother", IsAuthorizedForPickup = true },
            new StudentGuardian { Student = isha, Guardian = amit, Relationship = "Father", IsAuthorizedForPickup = true },
            new StudentGuardian { Student = kabir, Guardian = sunita, Relationship = "Mother", IsAuthorizedForPickup = true },
            // Rahul also has a secondary authorized guardian to demo multi-guardian pickup.
            new StudentGuardian { Student = rahul, Guardian = amit, Relationship = "Uncle", IsAuthorizedForPickup = true }
        );

        // --- Bus assignments ---
        db.StudentBusAssignments.AddRange(
            new StudentBusAssignment { Student = rahul, Bus = bus1, Route = routeA },
            new StudentBusAssignment { Student = isha, Bus = bus1, Route = routeA },
            new StudentBusAssignment { Student = kabir, Bus = bus2, Route = routeB }
        );

        await db.SaveChangesAsync();

        // --- A day of journey/attendance history for Rahul (yesterday, full cycle) ---
        var y = DateTime.UtcNow.Date.AddDays(-1);
        var events = new[]
        {
            (JourneyEventType.BusBoarding, y.AddHours(7).AddMinutes(15)),
            (JourneyEventType.SchoolArrival, y.AddHours(7).AddMinutes(55)),
            (JourneyEventType.SchoolDeparture, y.AddHours(14).AddMinutes(30)),
            (JourneyEventType.BusDropoff, y.AddHours(15).AddMinutes(10)),
        };
        foreach (var (type, ts) in events)
        {
            db.JourneyEvents.Add(new JourneyEvent
            {
                StudentId = rahul.StudentId,
                RfidCardId = cardRahul.RfidCardId,
                EventType = type,
                Timestamp = ts,
                Source = EventSource.Rfid
            });
        }
        db.AttendanceRecords.AddRange(
            new Attendance { StudentId = rahul.StudentId, RfidCardId = cardRahul.RfidCardId, EventType = AttendanceEventType.SchoolArrival, Date = DateOnly.FromDateTime(y), Time = TimeOnly.FromDateTime(y.AddHours(7).AddMinutes(55)) },
            new Attendance { StudentId = rahul.StudentId, RfidCardId = cardRahul.RfidCardId, EventType = AttendanceEventType.SchoolDeparture, Date = DateOnly.FromDateTime(y), Time = TimeOnly.FromDateTime(y.AddHours(14).AddMinutes(30)) }
        );

        db.Notifications.AddRange(
            new Notification { GuardianId = priya.GuardianId, StudentId = rahul.StudentId, Type = NotificationType.Journey, Title = "School Arrival recorded", Message = "Rahul Patel: School Arrival recorded at 7:55 AM.", SentAt = y.AddHours(7).AddMinutes(55) },
            new Notification { GuardianId = priya.GuardianId, StudentId = rahul.StudentId, Type = NotificationType.Journey, Title = "School Departure recorded", Message = "Rahul Patel: School Departure recorded at 2:30 PM.", SentAt = y.AddHours(14).AddMinutes(30) }
        );

        await db.SaveChangesAsync();
        logger.LogInformation("Demo data seeded: 3 students, 3 guardians, 2 buses, 1 day of history for Rahul Patel.");
    }
}
