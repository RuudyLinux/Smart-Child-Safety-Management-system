using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record StudentListItemDto(
    int StudentId, string FullName, string? Grade, RecordStatus Status,
    string? MaskedRfid, int GuardianCount);

public record StudentDetailDto(
    int StudentId, string FullName, DateTime DateOfBirth, string? Grade, RecordStatus Status,
    string? MaskedRfid, IReadOnlyList<GuardianSummaryDto> Guardians, DateTime CreatedAt);

public record GuardianSummaryDto(int GuardianId, string FullName, string Email, string? Relationship, bool IsAuthorizedForPickup);

public record CreateStudentRequest(string FullName, DateTime DateOfBirth, string? Grade);

public record UpdateStudentRequest(string FullName, DateTime DateOfBirth, string? Grade);

public record UpdateStudentStatusRequest(RecordStatus Status);

public record AssignGuardianRequest(int GuardianId, string? Relationship, bool IsAuthorizedForPickup);

/// <summary>Edit of an existing student-guardian link. The pair itself is identified by the route.</summary>
public record UpdateStudentGuardianRequest(string? Relationship, bool IsAuthorizedForPickup);
