using ChildGuard.Application.DTOs;
using ChildGuard.Application.Common;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IStudentRepository
{
    Task<List<Student>> GetAllAsync(string? search, CancellationToken ct = default);

    /// <summary>One page of students matching <paramref name="filter"/>; "today" values come from SchoolClock.</summary>
    Task<(List<Student> Items, int Total)> GetPageAsync(StudentListFilter filter, PageRequest page, DateOnly today, DateTime todayStartUtc, CancellationToken ct = default);

    /// <summary>Distinct non-empty grades, sorted, for the list's grade filter.</summary>
    Task<List<string>> GetGradesAsync(CancellationToken ct = default);
    Task<Student?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Student?> GetWithGuardiansAsync(int id, CancellationToken ct = default);
    Task AddAsync(Student student, CancellationToken ct = default);
    Task<StudentGuardian?> GetStudentGuardianAsync(int studentId, int guardianId, CancellationToken ct = default);
    Task AddStudentGuardianAsync(StudentGuardian link, CancellationToken ct = default);
    void RemoveStudentGuardian(StudentGuardian link);
    Task<List<StudentGuardian>> GetAuthorizedGuardiansAsync(int studentId, CancellationToken ct = default);
    Task<List<StudentGuardian>> GetLinkedGuardiansAsync(int studentId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    /// <summary>Every bus assignment of the student, newest first, with bus (and driver), route and stops.</summary>
    Task<List<StudentBusAssignment>> GetTransportHistoryAsync(int studentId, CancellationToken ct = default);
    /// <summary>Another student already has this admission number (case-insensitive).</summary>
    Task<bool> AdmissionNumberTakenAsync(string admissionNumber, int exceptStudentId, CancellationToken ct = default);
    /// <summary>Saves; false (nothing saved) when a unique index refused the write.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default);
}
