using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Services;

public class StudentService : IStudentService
{
    private readonly IStudentRepository _students;
    private readonly IGuardianRepository _guardians;

    public StudentService(IStudentRepository students, IGuardianRepository guardians)
    {
        _students = students;
        _guardians = guardians;
    }

    public async Task<List<StudentListItemDto>> GetAllAsync(string? search, CancellationToken ct = default)
    {
        var students = await _students.GetAllAsync(search, ct);
        return students.Select(ToListItem).ToList();
    }

    public async Task<Result<StudentDetailDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var student = await _students.GetWithGuardiansAsync(id, ct);
        return student is null
            ? Result<StudentDetailDto>.Failure("Student not found.")
            : Result<StudentDetailDto>.Success(ToDetail(student));
    }

    public async Task<Result<StudentDetailDto>> CreateAsync(CreateStudentRequest request, CancellationToken ct = default)
    {
        var student = new Student
        {
            FullName = request.FullName,
            DateOfBirth = request.DateOfBirth,
            Grade = request.Grade
        };
        await _students.AddAsync(student, ct);
        await _students.SaveChangesAsync(ct);
        return Result<StudentDetailDto>.Success(ToDetail(student));
    }

    public async Task<Result<StudentDetailDto>> UpdateAsync(int id, UpdateStudentRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(id, ct);
        if (student is null) return Result<StudentDetailDto>.Failure("Student not found.");

        student.FullName = request.FullName;
        student.DateOfBirth = request.DateOfBirth;
        student.Grade = request.Grade;
        student.UpdatedAt = DateTime.UtcNow;
        await _students.SaveChangesAsync(ct);

        var withGuardians = await _students.GetWithGuardiansAsync(id, ct);
        return Result<StudentDetailDto>.Success(ToDetail(withGuardians!));
    }

    public async Task<Result> UpdateStatusAsync(int id, UpdateStudentStatusRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(id, ct);
        if (student is null) return Result.Failure("Student not found.");

        student.Status = request.Status;
        student.UpdatedAt = DateTime.UtcNow;
        await _students.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> AssignGuardianAsync(int studentId, AssignGuardianRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(studentId, ct);
        if (student is null) return Result.Failure("Student not found.");

        var guardian = await _guardians.GetByIdAsync(request.GuardianId, ct);
        if (guardian is null) return Result.Failure("Guardian not found.");

        var existing = await _students.GetStudentGuardianAsync(studentId, request.GuardianId, ct);
        if (existing is not null)
            return Result.Failure("Guardian already associated with this student.");

        await _students.AddStudentGuardianAsync(new StudentGuardian
        {
            StudentId = studentId,
            GuardianId = request.GuardianId,
            Relationship = request.Relationship,
            IsAuthorizedForPickup = request.IsAuthorizedForPickup
        }, ct);
        await _students.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateGuardianLinkAsync(int studentId, int guardianId, UpdateStudentGuardianRequest request, CancellationToken ct = default)
    {
        var link = await _students.GetStudentGuardianAsync(studentId, guardianId, ct);
        if (link is null) return Result.Failure("Guardian is not linked to this student.");

        link.Relationship = request.Relationship;
        link.IsAuthorizedForPickup = request.IsAuthorizedForPickup;
        await _students.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveGuardianLinkAsync(int studentId, int guardianId, CancellationToken ct = default)
    {
        var link = await _students.GetStudentGuardianAsync(studentId, guardianId, ct);
        if (link is null) return Result.Failure("Guardian is not linked to this student.");

        _students.RemoveStudentGuardian(link);
        await _students.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static StudentListItemDto ToListItem(Student s) => new(
        s.StudentId, s.FullName, s.Grade, s.Status,
        s.RfidCard is not null ? RfidMasking.Mask(s.RfidCard.CardUid) : null,
        s.StudentGuardians.Count);

    private static StudentDetailDto ToDetail(Student s) => new(
        s.StudentId, s.FullName, s.DateOfBirth, s.Grade, s.Status,
        s.RfidCard is not null ? RfidMasking.Mask(s.RfidCard.CardUid) : null,
        s.StudentGuardians.Select(sg => new GuardianSummaryDto(
            sg.GuardianId, sg.Guardian.FullName, sg.Guardian.Email, sg.Relationship, sg.IsAuthorizedForPickup)).ToList(),
        s.CreatedAt);
}
