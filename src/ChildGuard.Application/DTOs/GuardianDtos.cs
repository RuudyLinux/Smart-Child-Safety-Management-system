using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record GuardianListItemDto(int GuardianId, string FullName, string Email, string? Phone, RecordStatus Status, int LinkedStudentCount);

public record GuardianDetailDto(
    int GuardianId, string FullName, string Email, string? Phone, RecordStatus Status,
    bool FaceRegistered, IReadOnlyList<StudentSummaryDto> Students, DateTime CreatedAt);

public record StudentSummaryDto(int StudentId, string FullName, string? Grade, string? Relationship, bool IsAuthorizedForPickup);

public record UpdateGuardianStatusRequest(RecordStatus Status);

public record UpdateProfileRequest(string FullName, string? Phone);

public record RegisterDeviceTokenRequest(string FcmToken);
