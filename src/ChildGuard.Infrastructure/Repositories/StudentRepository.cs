using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
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

        if (!string.IsNullOrWhiteSpace(search))
        {
            // MySQL's collation makes Contains case-insensitive; PostgreSQL needs ILIKE to match.
            if (_db.Database.IsNpgsql())
            {
                var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = query.Where(s => EF.Functions.ILike(s.FullName, pattern, "\\"));
            }
            else
                query = query.Where(s => s.FullName.Contains(search));
        }

        return await query.OrderBy(s => s.FullName).ToListAsync(ct);
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
}
