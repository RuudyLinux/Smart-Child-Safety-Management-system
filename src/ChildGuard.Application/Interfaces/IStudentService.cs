using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IStudentService
{
    Task<List<StudentListItemDto>> GetAllAsync(string? search, CancellationToken ct = default);
    Task<Result<StudentDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<StudentDetailDto>> CreateAsync(CreateStudentRequest request, CancellationToken ct = default);
    Task<Result<StudentDetailDto>> UpdateAsync(int id, UpdateStudentRequest request, CancellationToken ct = default);
    Task<Result> UpdateStatusAsync(int id, UpdateStudentStatusRequest request, CancellationToken ct = default);
    Task<Result> AssignGuardianAsync(int studentId, AssignGuardianRequest request, CancellationToken ct = default);
}
