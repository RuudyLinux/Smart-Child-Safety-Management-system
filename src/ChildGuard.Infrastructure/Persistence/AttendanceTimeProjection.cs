using ChildGuard.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Persistence;

/// <summary>
/// Brings Attendance.Date/Time onto the school calendar for rows written before
/// School:TimeZone existed. Those rows were stamped from DateTime.UtcNow (every
/// writer — RFID scan and demo seeder — used UTC), and migration
/// AddAttendanceRecordedAtUtc copied that original instant into RecordedAtUtc.
///
/// Safe to run on every startup and impossible to apply twice: it only touches rows
/// whose Date/Time still equal the UTC projection of RecordedAtUtc, and rewrites
/// them to the school-local projection of the same instant. A converted row no
/// longer matches the UTC projection (unless the zone's offset is zero then, in
/// which case local == UTC and there is nothing to change). RecordedAtUtc itself is
/// never modified, so the original value is always recoverable.
/// </summary>
public static class AttendanceTimeProjection
{
    public static async Task<int> SyncAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChildGuardDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<SchoolClock>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SchoolClock>>();

        var candidates = await db.AttendanceRecords
            .FromSqlRaw("SELECT * FROM AttendanceRecords WHERE `Date` = DATE(RecordedAtUtc) AND `Time` = TIME(RecordedAtUtc)")
            .ToListAsync(ct);

        var changed = 0;
        foreach (var row in candidates)
        {
            var (date, time) = clock.ToSchoolDateTime(row.RecordedAtUtc);
            if (row.Date == date && row.Time == time) continue;
            row.Date = date;
            row.Time = time;
            changed++;
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Attendance time projection: converted {Count} legacy UTC row(s) to school time zone {TimeZone}.",
                changed, clock.TimeZone.Id);
        }
        return changed;
    }
}
