using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IStudentRepository
{
    Task<List<Student>> GetAllAsync(string? search, CancellationToken ct = default);
    Task<Student?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Student?> GetWithGuardiansAsync(int id, CancellationToken ct = default);
    Task AddAsync(Student student, CancellationToken ct = default);
    Task<StudentGuardian?> GetStudentGuardianAsync(int studentId, int guardianId, CancellationToken ct = default);
    Task AddStudentGuardianAsync(StudentGuardian link, CancellationToken ct = default);
    void RemoveStudentGuardian(StudentGuardian link);
    Task<List<StudentGuardian>> GetAuthorizedGuardiansAsync(int studentId, CancellationToken ct = default);
    Task<List<StudentGuardian>> GetLinkedGuardiansAsync(int studentId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    /// <summary>Another student already has this admission number (case-insensitive).</summary>
    Task<bool> AdmissionNumberTakenAsync(string admissionNumber, int exceptStudentId, CancellationToken ct = default);
    /// <summary>Saves; false (nothing saved) when a unique index refused the write.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default);
}
