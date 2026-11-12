using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

/// <summary>
/// Read-only views for the Guardian app. Every method takes the signed-in guardian's id and only ever
/// returns data for students linked to that guardian; a student id that is not linked reads as not found.
/// </summary>
public interface IGuardianPortalReadService
{
    /// <summary>True when the student is linked to the guardian.</summary>
    Task<bool> IsLinkedAsync(int guardianId, int studentId, CancellationToken ct = default);

    Task<GuardianDashboardDto?> GetDashboardAsync(int guardianId, CancellationToken ct = default);

    Task<List<GuardianChildCardDto>> GetChildrenAsync(int guardianId, CancellationToken ct = default);

    Task<GuardianStudentDetailDto?> GetStudentDetailAsync(int guardianId, int studentId, CancellationToken ct = default);

    /// <summary>Journey events for one child over school-calendar dates (inclusive), newest first.</summary>
    Task<List<JourneyEventDto>> GetJourneyAsync(int guardianId, int studentId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<Result<GuardianStudentReportDto>> GetReportAsync(int guardianId, int studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>Pickup attempts (permanent guardians and temporary pickup people) for the guardian's children, newest first.</summary>
    Task<List<GuardianPickupDto>> GetPickupsAsync(int guardianId, int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}
