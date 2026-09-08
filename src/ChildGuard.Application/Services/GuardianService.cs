using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;

namespace ChildGuard.Application.Services;

public class GuardianService : IGuardianService
{
    private readonly IGuardianRepository _guardians;

    public GuardianService(IGuardianRepository guardians) => _guardians = guardians;

    public async Task<List<GuardianListItemDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _guardians.GetAllAsync(ct);
        return list.Select(g => new GuardianListItemDto(
            g.GuardianId, g.FullName, g.Email, g.Phone, g.Status, g.StudentGuardians.Count)).ToList();
    }

    public async Task<Result<GuardianDetailDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var g = await _guardians.GetWithStudentsAsync(id, ct);
        if (g is null) return Result<GuardianDetailDto>.Failure("Guardian not found.");

        return Result<GuardianDetailDto>.Success(new GuardianDetailDto(
            g.GuardianId, g.FullName, g.Email, g.Phone, g.Status,
            g.FaceData is not null && g.FaceData.IsActive,
            g.StudentGuardians.Select(sg => new StudentSummaryDto(
                sg.StudentId, sg.Student.FullName, sg.Student.Grade, sg.Relationship, sg.IsAuthorizedForPickup)).ToList(),
            g.CreatedAt));
    }

    public async Task<Result> UpdateStatusAsync(int id, UpdateGuardianStatusRequest request, CancellationToken ct = default)
    {
        var g = await _guardians.GetByIdAsync(id, ct);
        if (g is null) return Result.Failure("Guardian not found.");
        g.Status = request.Status;
        g.UpdatedAt = DateTime.UtcNow;
        await _guardians.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateProfileAsync(int id, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var g = await _guardians.GetByIdAsync(id, ct);
        if (g is null) return Result.Failure("Guardian not found.");
        g.FullName = request.FullName;
        g.Phone = request.Phone;
        g.UpdatedAt = DateTime.UtcNow;
        await _guardians.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RegisterDeviceTokenAsync(int id, string fcmToken, CancellationToken ct = default)
    {
        var g = await _guardians.GetByIdAsync(id, ct);
        if (g is null) return Result.Failure("Guardian not found.");
        g.FcmToken = fcmToken;
        await _guardians.SaveChangesAsync(ct);
        return Result.Success();
    }
}
