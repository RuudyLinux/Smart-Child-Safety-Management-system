using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class StudentRepository : IStudentRepository
{
    private readonly ChildGuardDbContext _db;
    public StudentRepository(ChildGuardDbContext db) => _db = db;

    public async Task<List<Student>> GetAllAsync(string? search, CancellationToken ct = default)
    {
        var query = _db.Students
            .Include(s => s.RfidCard)
            // Guardian is included so the list can name the primary guardian without
            // a second round trip per row.
            .Include(s => s.StudentGuardians).ThenInclude(sg => sg.Guardian)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search)) query = WhereNameContains(query, search);

        return await query.OrderBy(s => s.FullName).ToListAsync(ct);
    }

    /// <summary>MySQL's collation makes Contains case-insensitive; PostgreSQL needs ILIKE to match.</summary>
    private IQueryable<Student> WhereNameContains(IQueryable<Student> query, string search)
    {
        if (!_db.Database.IsNpgsql()) return query.Where(s => s.FullName.Contains(search));
        var pattern = "%" + LikeEscape(search) + "%";
        return query.Where(s => EF.Functions.ILike(s.FullName, pattern, "\\"));
    }

    private static string LikeEscape(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<(List<Student> Items, int Total)> GetPageAsync(
        StudentListFilter filter, PageRequest page, DateOnly today, DateTime todayStartUtc, CancellationToken ct = default)
    {
        var query = _db.Students.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search)) query = WhereNameContains(query, filter.Search.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Grade))
        {
            var grade = filter.Grade.Trim();
            query = _db.Database.IsNpgsql()
                ? query.Where(s => s.Grade != null && EF.Functions.ILike(s.Grade, LikeEscape(grade), "\\"))
                : query.Where(s => s.Grade == grade);
        }
        query = filter.Status switch
        {
            ListStatusFilter.Archived => query.Where(s => s.ArchivedAt != null),
            ListStatusFilter.Active => query.Where(s => s.ArchivedAt == null && s.Status == RecordStatus.Active),
            ListStatusFilter.Inactive => query.Where(s => s.ArchivedAt == null && s.Status == RecordStatus.Inactive),
            _ => query
        };
        if (filter.HasRfid is bool hasRfid) query = query.Where(s => (s.RfidCard != null) == hasRfid);
        if (filter.HasGuardian is bool hasGuardian) query = query.Where(s => s.StudentGuardians.Any() == hasGuardian);
        if (filter.PresentToday is bool present)
            // Same rule as the safety snapshot: a school arrival on today's school date.
            query = query.Where(s => _db.AttendanceRecords.Any(a =>
                a.StudentId == s.StudentId && a.Date == today && a.EventType == AttendanceEventType.SchoolArrival) == present);

        var total = await query.CountAsync(ct);

        var ordered = Order(query, page, todayStartUtc);
        var items = await ordered
            .Skip(page.Skip).Take(page.PageSize)
            .Include(s => s.RfidCard)
            .Include(s => s.StudentGuardians).ThenInclude(sg => sg.Guardian)
            .ToListAsync(ct);
        return (items, total);
    }

    /// <summary>
    /// Sorts with a stable final key (StudentId) so pages never overlap. Missing values sort the same way on
    /// both engines (MySQL and PostgreSQL disagree on where NULLs go): no grade as "", no activity first when
    /// ascending and last when descending.
    /// </summary>
    private IOrderedQueryable<Student> Order(IQueryable<Student> query, PageRequest page, DateTime todayStartUtc)
    {
        var desc = page.Descending;
        IOrderedQueryable<Student> ordered;
        switch (page.Sort)
        {
            case "grade":
                // Lower-cased: MySQL compares grades case-insensitively, PostgreSQL text does not.
                ordered = desc ? query.OrderByDescending(s => (s.Grade ?? "").ToLower()) : query.OrderBy(s => (s.Grade ?? "").ToLower());
                ordered = ordered.ThenBy(s => s.FullName);
                break;
            case "status":
                // Active, Inactive, then Archived (descending reverses it).
                ordered = desc
                    ? query.OrderByDescending(s => s.ArchivedAt != null ? 3 : (int)s.Status)
                    : query.OrderBy(s => s.ArchivedAt != null ? 3 : (int)s.Status);
                ordered = ordered.ThenBy(s => s.FullName);
                break;
            case "activity":
                // Last activity = the latest journey event since the school day began, as on the list.
                var events = _db.JourneyEvents;
                ordered = desc
                    ? query.OrderBy(s => events.Any(e => e.StudentId == s.StudentId && e.Timestamp >= todayStartUtc) ? 0 : 1)
                        .ThenByDescending(s => events.Where(e => e.StudentId == s.StudentId && e.Timestamp >= todayStartUtc).Max(e => (DateTime?)e.Timestamp))
                    : query.OrderBy(s => events.Any(e => e.StudentId == s.StudentId && e.Timestamp >= todayStartUtc) ? 1 : 0)
                        .ThenBy(s => events.Where(e => e.StudentId == s.StudentId && e.Timestamp >= todayStartUtc).Max(e => (DateTime?)e.Timestamp));
                ordered = ordered.ThenBy(s => s.FullName);
                break;
            default:
                ordered = desc ? query.OrderByDescending(s => s.FullName) : query.OrderBy(s => s.FullName);
                break;
        }
        return ordered.ThenBy(s => s.StudentId);
    }

    /// <summary>
    /// One entry per grade, case-insensitively (as the grade filter matches), spelled the way most students
    /// have it. Grouped here rather than in SQL so MySQL and PostgreSQL give the same list.
    /// </summary>
    public async Task<List<string>> GetGradesAsync(CancellationToken ct = default)
    {
        var spellings = await _db.Students.AsNoTracking()
            .Where(s => s.Grade != null && s.Grade != "")
            .GroupBy(s => s.Grade!)
            .Select(g => new { Grade = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return spellings
            .GroupBy(x => x.Grade, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Count).ThenBy(x => x.Grade, StringComparer.Ordinal).First().Grade)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<Student?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Students.Include(s => s.RfidCard).FirstOrDefaultAsync(s => s.StudentId == id, ct);

    public Task<Student?> GetWithGuardiansAsync(int id, CancellationToken ct = default) =>
        _db.Students
            .Include(s => s.RfidCard)
            .Include(s => s.StudentGuardians).ThenInclude(sg => sg.Guardian).ThenInclude(g => g.FaceData)
            .FirstOrDefaultAsync(s => s.StudentId == id, ct);

    public async Task AddAsync(Student student, CancellationToken ct = default) =>
        await _db.Students.AddAsync(student, ct);

    public Task<StudentGuardian?> GetStudentGuardianAsync(int studentId, int guardianId, CancellationToken ct = default) =>
        _db.StudentGuardians.FirstOrDefaultAsync(sg => sg.StudentId == studentId && sg.GuardianId == guardianId, ct);

    public async Task AddStudentGuardianAsync(StudentGuardian link, CancellationToken ct = default) =>
        await _db.StudentGuardians.AddAsync(link, ct);

    public void RemoveStudentGuardian(StudentGuardian link) =>
        _db.StudentGuardians.Remove(link);

    public Task<List<StudentGuardian>> GetAuthorizedGuardiansAsync(int studentId, CancellationToken ct = default) =>
        _db.StudentGuardians
            .Include(sg => sg.Guardian)
            .Where(sg => sg.StudentId == studentId && sg.IsAuthorizedForPickup)
            .ToListAsync(ct);

    public Task<List<StudentGuardian>> GetLinkedGuardiansAsync(int studentId, CancellationToken ct = default) =>
        _db.StudentGuardians
            .Include(sg => sg.Guardian)
            .Where(sg => sg.StudentId == studentId)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public Task<List<StudentBusAssignment>> GetTransportHistoryAsync(int studentId, CancellationToken ct = default) =>
        _db.StudentBusAssignments.AsNoTracking()
            .Include(a => a.Bus).ThenInclude(b => b.Driver)
            .Include(a => a.Route).Include(a => a.PickupStop).Include(a => a.DropStop)
            .Where(a => a.StudentId == studentId)
            .OrderByDescending(a => a.AssignedAt).ThenByDescending(a => a.Id)
            .ToListAsync(ct);

    public Task<bool> AdmissionNumberTakenAsync(string admissionNumber, int exceptStudentId, CancellationToken ct = default) =>
        _db.Students.AnyAsync(s => s.AdmissionNumber == admissionNumber && s.StudentId != exceptStudentId, ct);

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }
}
