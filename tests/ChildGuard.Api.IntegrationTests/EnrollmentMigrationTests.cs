using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// The EnrollmentAndAddresses migration, on this class's throwaway database (MySQL or PostgreSQL):
///  * on a clean database it is applied and nothing is pending;
///  * on a database holding students, guardians, links, RFID cards and bus assignments written at the previous
///    schema, every existing row is byte-for-byte unchanged, new columns are NULL, each existing card link becomes
///    one history row flagged as a migrated snapshot with the card's own AssignedAt (NULL stays NULL), and the API
///    works with the legacy records afterwards. Migrating down and up again also exercises the rollback path.
/// </summary>
public class EnrollmentMigrationTests : IClassFixture<ApiFactory>
{
    private const string PreviousMigration = "TransportIntegrityRules";
    private const string ThisMigration = "EnrollmentAndAddresses";
    private readonly ApiFactory _api;
    public EnrollmentMigrationTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task CleanDatabase_HasTheMigrationApplied_AndNothingPending()
    {
        await using var db = _api.NewDb();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_" + ThisMigration, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExistingData_SurvivesUnchanged_AndCardLinksAreBackfilledWithoutInventedDates()
    {
        await using var db = _api.NewDb();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        // ---------------- legacy data, written at the previous schema
        var created = new DateTime(2026, 4, 1, 3, 0, 0, DateTimeKind.Utc);
        var dated = new DateTime(2026, 6, 1, 3, 15, 0, DateTimeKind.Utc);
        var route = await Insert(conn, "Routes", "RouteId", ("RouteName", "Legacy Route " + Guid.NewGuid().ToString("N")[..6], DbType.String),
            ("Description", null, DbType.String), ("Status", 1, DbType.Int32), ("CreatedAt", created, DbType.DateTime));
        var driver = await Insert(conn, "Drivers", "DriverId", ("FullName", "Legacy Driver", DbType.String), ("Phone", "9800000001", DbType.String),
            ("LicenseNumber", "DL-LEGACY", DbType.String), ("Status", 1, DbType.Int32), ("CreatedAt", created, DbType.DateTime));
        var bus = await Insert(conn, "Buses", "BusId", ("BusNumber", "LEG-" + Guid.NewGuid().ToString("N")[..6], DbType.String), ("Capacity", 30, DbType.Int32),
            ("DriverId", driver, DbType.Int32), ("RouteId", route, DbType.Int32), ("Status", 1, DbType.Int32), ("CreatedAt", created, DbType.DateTime));

        async Task<int> Student(string name, int status) => await Insert(conn, "Students", "StudentId", ("FullName", name, DbType.String),
            ("DateOfBirth", new DateTime(2015, 3, 3), DbType.DateTime2), ("Grade", "3A", DbType.String), ("Status", status, DbType.Int32),
            ("CreatedAt", created, DbType.DateTime), ("UpdatedAt", created, DbType.DateTime));
        var s1 = await Student("Legacy Anaya", 1);
        var s2 = await Student("Legacy Vihaan", 1);
        var s3 = await Student("Legacy Archived", 2);

        async Task<int> Guardian(string name, int status, bool reset) => await Insert(conn, "Guardians", "GuardianId", ("FullName", name, DbType.String),
            ("Email", $"{Guid.NewGuid():N}@legacy.test", DbType.String), ("Phone", "97" + Random.Shared.Next(10000000, 99999999), DbType.String),
            ("PasswordHash", "legacy-hash-" + name, DbType.String), ("Status", status, DbType.Int32), ("PasswordResetRequired", reset, DbType.Boolean),
            ("CreatedAt", created, DbType.DateTime), ("UpdatedAt", created, DbType.DateTime));
        var g1 = await Guardian("Legacy Priya", 1, false);
        var g2 = await Guardian("Legacy Inactive", 2, false);
        var g3 = await Guardian("Legacy Rohan", 1, true);

        async Task Link(int s, int g, string rel, bool auth) => await Insert(conn, "StudentGuardians", "Id", ("StudentId", s, DbType.Int32),
            ("GuardianId", g, DbType.Int32), ("Relationship", rel, DbType.String), ("IsAuthorizedForPickup", auth, DbType.Boolean), ("CreatedAt", created, DbType.DateTime));
        await Link(s1, g1, "Mother", true);
        await Link(s2, g1, "Mother", true);
        await Link(s2, g3, "Father", false);
        await Link(s3, g2, "Guardian", true);

        string datedUid = "MIG-D-" + Guid.NewGuid().ToString("N")[..8], undatedUid = "MIG-U-" + Guid.NewGuid().ToString("N")[..8];
        string spareUid = "MIG-S-" + Guid.NewGuid().ToString("N")[..8], archivedUid = "MIG-A-" + Guid.NewGuid().ToString("N")[..8];
        async Task<int> Card(string uid, int? s, int status, DateTime? at) => await Insert(conn, "RfidCards", "RfidCardId", ("CardUid", uid, DbType.String),
            ("StudentId", s, DbType.Int32), ("Status", status, DbType.Int32), ("AssignedAt", at, DbType.DateTime), ("CreatedAt", created, DbType.DateTime));
        var cDated = await Card(datedUid, s1, 1, dated);
        var cUndated = await Card(undatedUid, s2, 1, null);
        var cSpare = await Card(spareUid, null, 3, null);
        var cArchived = await Card(archivedUid, s3, 2, dated);

        await Insert(conn, "StudentBusAssignments", "Id", ("StudentId", s1, DbType.Int32), ("BusId", bus, DbType.Int32), ("RouteId", route, DbType.Int32),
            ("Status", 1, DbType.Int32), ("AssignedAt", dated, DbType.DateTime), ("EndedAt", null, DbType.DateTime));
        await Insert(conn, "StudentBusAssignments", "Id", ("StudentId", s2, DbType.Int32), ("BusId", bus, DbType.Int32), ("RouteId", route, DbType.Int32),
            ("Status", 2, DbType.Int32), ("AssignedAt", created, DbType.DateTime), ("EndedAt", dated, DbType.DateTime));

        var before = await SnapshotAsync(conn);
        await conn.CloseAsync();

        // ---------------- the migration under test
        await migrator.MigrateAsync();

        await using var after = _api.NewDb();
        var afterConn = after.Database.GetDbConnection();
        await afterConn.OpenAsync();
        var snapshot = await SnapshotAsync(afterConn);
        foreach (var table in before.Keys)
            Assert.True(before[table].SequenceEqual(snapshot[table]), $"{table} changed:\n{string.Join("\n", before[table])}\n--- after ---\n{string.Join("\n", snapshot[table])}");
        await afterConn.CloseAsync();

        var history = await after.RfidCardAssignments.AsNoTracking().OrderBy(h => h.RfidCardId).ToListAsync();
        Assert.Equal(new[] { cDated, cUndated, cArchived }, history.Select(h => h.RfidCardId));
        Assert.All(history, h => Assert.True(h.IsMigratedSnapshot));
        Assert.All(history, h => Assert.Null(h.EndedAt));
        Assert.All(history, h => Assert.Null(h.AssignedByAdminId));
        Assert.Equal(dated, history.Single(h => h.RfidCardId == cDated).AssignedAt);
        Assert.Null(history.Single(h => h.RfidCardId == cUndated).AssignedAt);
        Assert.Equal(s3, history.Single(h => h.RfidCardId == cArchived).StudentId);
        Assert.DoesNotContain(history, h => h.RfidCardId == cSpare);

        foreach (var s in await after.Students.AsNoTracking().Where(x => x.StudentId == s1 || x.StudentId == s2 || x.StudentId == s3).ToListAsync())
        {
            Assert.Null(s.Address);
            Assert.Null(s.AdmissionNumber);
            Assert.Null(s.Gender);
            Assert.Null(s.Section);
        }
        Assert.All(await after.Guardians.AsNoTracking().Where(x => x.GuardianId == g1 || x.GuardianId == g2 || x.GuardianId == g3).ToListAsync(),
            g => Assert.Null(g.Address));
        Assert.Empty(await after.EnrollmentRequests.ToListAsync());

        // ---------------- the API works with the legacy records
        var admin = await _api.AdminClientAsync();
        var student = await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{s1}"));
        Assert.Null(student.Address);
        Assert.Equal("Legacy Priya", Assert.Single(student.Guardians).FullName);
        var guardian = await Ok<GuardianDetailDto>(await admin.GetAsync($"/api/guardians/{g1}"));
        Assert.Equal(2, guardian.Students.Count);
        var check = await Ok<RfidCardCheckDto>(await admin.PostAsJsonAsync("/api/rfid/cards/check", new RfidCardCheckRequest(datedUid), Json.Options));
        Assert.Equal((RfidCardAvailability.AssignedToStudent, s1), (check.Availability, check.StudentId));

        var device = await after.RfidDevices.AsNoTracking().OrderBy(d => d.RfidDeviceId).FirstAsync();
        var scan = await Ok<RfidScanResultDto>(await admin.PostAsJsonAsync("/api/rfid/scan",
            new RfidScanRequest(datedUid, device.RfidDeviceId, JourneyEventType.BusBoarding), Json.Options));
        Assert.True(scan.EventRecorded);

        var enrolled = await admin.PostAsJsonAsync("/api/enrollments", new EnrollStudentRequest(Guid.NewGuid().ToString("N"),
            new EnrollmentStudentDto("Legacy Sibling", new DateTime(2018, 8, 8), "1", null, null, null, null),
            [new EnrollmentGuardianDto(g1, null, null, null, null, false, "Mother", true)], null, null), Json.Options);
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
        Assert.Equal(3, (await Ok<GuardianDetailDto>(await admin.GetAsync($"/api/guardians/{g1}"))).Students.Count);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private static readonly (string Table, string Key, string[] Columns)[] Tables =
    {
        ("Students", "StudentId", new[] { "StudentId", "FullName", "DateOfBirth", "Grade", "Status", "CreatedAt", "UpdatedAt" }),
        ("Guardians", "GuardianId", new[] { "GuardianId", "FullName", "Email", "Phone", "PasswordHash", "Status", "PasswordResetRequired", "CreatedAt", "UpdatedAt" }),
        ("StudentGuardians", "Id", new[] { "Id", "StudentId", "GuardianId", "Relationship", "IsAuthorizedForPickup", "CreatedAt" }),
        ("RfidCards", "RfidCardId", new[] { "RfidCardId", "CardUid", "StudentId", "Status", "AssignedAt", "CreatedAt" }),
        ("StudentBusAssignments", "Id", new[] { "Id", "StudentId", "BusId", "RouteId", "Status", "AssignedAt", "EndedAt", "ActiveStudentKey" }),
        ("Buses", "BusId", new[] { "BusId", "BusNumber", "Capacity", "DriverId", "RouteId", "Status", "CreatedAt" }),
        ("Drivers", "DriverId", new[] { "DriverId", "FullName", "Phone", "LicenseNumber", "Status", "CreatedAt" }),
        ("Routes", "RouteId", new[] { "RouteId", "RouteName", "Description", "Status", "CreatedAt" }),
        ("Admins", "AdminId", new[] { "AdminId", "Email", "PasswordHash", "Status" }),
    };

    /// <summary>Every row of the pre-existing tables as text, in key order.</summary>
    private async Task<Dictionary<string, List<string>>> SnapshotAsync(DbConnection conn)
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var (table, key, columns) in Tables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {string.Join(", ", columns.Select(_api.Q))} FROM {_api.Q(table)} ORDER BY {_api.Q(key)}";
            var rows = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var values = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    values[i] = reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)!;
                rows.Add(string.Join(" | ", values));
            }
            result[table] = rows;
        }
        return result;
    }

    /// <summary>Inserts one row with typed parameters (works before the current model exists) and returns its id.</summary>
    private async Task<int> Insert(DbConnection conn, string table, string idColumn, params (string Column, object? Value, DbType Type)[] values)
    {
        await using var cmd = conn.CreateCommand();
        var names = new List<string>();
        for (var i = 0; i < values.Length; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@p" + i;
            p.DbType = values[i].Type;
            p.Value = values[i].Value ?? DBNull.Value;
            cmd.Parameters.Add(p);
            names.Add("@p" + i);
        }
        var insert = $"INSERT INTO {_api.Q(table)} ({string.Join(", ", values.Select(v => _api.Q(v.Column)))}) VALUES ({string.Join(", ", names)})";
        cmd.CommandText = _api.Provider == ChildGuard.Infrastructure.Persistence.DatabaseProvider.Postgres
            ? insert + $" RETURNING {_api.Q(idColumn)}"
            : insert + "; SELECT LAST_INSERT_ID();";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
