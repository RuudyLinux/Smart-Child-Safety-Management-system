using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class GuardianService : IGuardianService
{
    /// <summary>Recent verification attempts shown on a guardian profile.</summary>
    private const int ProfileHistoryRows = 10;

    private readonly IGuardianRepository _guardians;
    private readonly IPasswordHasherService _hasher;
    private readonly IEmailSender _email;
    private readonly IPhotoRepository _photos;
    private readonly ISafetyReadService _safety;
    private readonly IVerificationRepository _verifications;

    public GuardianService(
        IGuardianRepository guardians,
        IPasswordHasherService hasher,
        IEmailSender email,
        IPhotoRepository photos,
        ISafetyReadService safety,
        IVerificationRepository verifications)
    {
        _guardians = guardians;
        _hasher = hasher;
        _email = email;
        _photos = photos;
        _safety = safety;
        _verifications = verifications;
    }

    public async Task<Result<CreateGuardianResultDto>> CreateAsync(CreateGuardianRequest request, CancellationToken ct = default)
    {
        if (await _guardians.EmailExistsAsync(request.Email, ct))
            return Result<CreateGuardianResultDto>.Failure("Email already registered.");

        var contactNumber = ContactNumber.Normalize(request.ContactNumber);
        if (contactNumber is null)
            return Result<CreateGuardianResultDto>.Failure("A valid contact number is required.");
        if (await _guardians.ContactNumberExistsAsync(contactNumber, ct))
            return Result<CreateGuardianResultDto>.Failure("Contact number already registered.");

        // Random per Guardian. Hashed before it ever reaches the database; the
        // plaintext exists only in this method and in the email sent to the Guardian.
        var initialPassword = GuardianCredentialDefaults.NewInitialPassword();
        var guardian = new Guardian
        {
            FullName = request.FullName,
            Email = request.Email,
            Phone = contactNumber,
            PasswordHash = _hasher.Hash(initialPassword),
            PasswordResetRequired = true,
            Status = RecordStatus.Active
        };

        await _guardians.AddAsync(guardian, ct);
        await _guardians.SaveChangesAsync(ct);

        // Sent to the Guardian's own registered address, never the admin's.
        var message = GuardianEmails.BuildCredentialEmail(
            guardian.FullName, guardian.Email, contactNumber, initialPassword);
        var delivery = await _email.SendAsync(message, ct);

        // A delivery failure does not undo the account, but it is reported to the
        // admin rather than swallowed.
        return Result<CreateGuardianResultDto>.Success(
            new CreateGuardianResultDto(guardian.GuardianId, delivery.Succeeded, delivery.Succeeded ? null : delivery.Error));
    }

    public async Task<List<GuardianListItemDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _guardians.GetAllAsync(ct);
        if (list.Count == 0) return new List<GuardianListItemDto>();

        var ids = list.Select(g => g.GuardianId).ToList();
        var photos = await _photos.GetGuardianPhotoTimestampsAsync(ids, ct);
        var lastActivity = await _verifications.GetLastActivityByGuardianAsync(ct);

        return list.Select(g => new GuardianListItemDto(
            g.GuardianId, g.FullName, g.Email, g.Phone, g.Status, g.StudentGuardians.Count,
            photos.ContainsKey(g.GuardianId),
            photos.TryGetValue(g.GuardianId, out var at) ? at : null,
            g.FaceData is not null && g.FaceData.IsActive,
            g.PasswordResetRequired,
            lastActivity.TryGetValue(g.GuardianId, out var lastAt) ? lastAt : null)).ToList();
    }

    public async Task<Result<GuardianDetailDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var g = await _guardians.GetWithStudentsAsync(id, ct);
        if (g is null) return Result<GuardianDetailDto>.Failure("Guardian not found.");

        var photos = await _photos.GetGuardianPhotoTimestampsAsync(new[] { id }, ct);
        var studentIds = g.StudentGuardians.Select(sg => sg.StudentId).ToList();
        var snapshots = await _safety.GetSnapshotsAsync(studentIds, ct);

        var history = (await _verifications.GetHistoryForGuardianAsync(id, ProfileHistoryRows, ct))
            .Select(v => new GuardianVerificationHistoryDto(
                v.Id, v.StudentId, v.Student.FullName, v.GuardianId, v.Guardian.FullName,
                v.AttemptedAt, v.MatchScore, v.Result, v.PickupApproved,
                Layers: FaceLayersDto.From(v)))
            .ToList();

        var children = g.StudentGuardians.Select(sg =>
        {
            var snap = snapshots.TryGetValue(sg.StudentId, out var found)
                ? found
                : StudentSafetySnapshot.Unknown(sg.StudentId);
            return new StudentSummaryDto(
                sg.StudentId, sg.Student.FullName, sg.Student.Grade, sg.Relationship, sg.IsAuthorizedForPickup,
                snap.HasPhoto, snap.Status, snap.LastEventAt, snap.LastEventType,
                snap.CheckInTime, snap.PresentToday, snap.BusNumber, snap.RouteName);
        }).ToList();

        return Result<GuardianDetailDto>.Success(new GuardianDetailDto(
            g.GuardianId, g.FullName, g.Email, g.Phone, g.Status,
            g.FaceData is not null && g.FaceData.IsActive,
            g.PasswordResetRequired,
            children,
            g.CreatedAt,
            photos.ContainsKey(id),
            photos.TryGetValue(id, out var photoAt) ? photoAt : null,
            history.FirstOrDefault()?.AttemptedAt,
            history,
            AddressDto.From(g.Address)));
    }

    public async Task<List<GuardianLookupDto>> LookupAsync(string query, CancellationToken ct = default)
    {
        var text = query?.Trim() ?? "";
        if (text.Length < 3) return new List<GuardianLookupDto>();
        var digits = new string(text.Where(char.IsDigit).ToArray());
        var normalized = ContactNumber.Normalize(text);

        var found = await _guardians.SearchAsync(text, digits.Length >= 4 ? digits : null, 10, ct);
        // An exact email or contact-number match is the account the admin is most likely looking for.
        return found
            .OrderByDescending(g => string.Equals(g.Email, text, StringComparison.OrdinalIgnoreCase) || (normalized is not null && g.Phone == normalized))
            .ThenBy(g => g.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GuardianLookupDto(
                g.GuardianId, g.FullName, g.Email, g.Phone, g.Status,
                g.FaceData is not null && g.FaceData.IsActive,
                g.StudentGuardians.Select(sg => sg.Student.FullName).OrderBy(n => n).ToList()))
            .ToList();
    }

    public async Task<Result> AdminUpdateAsync(int id, AdminUpdateGuardianRequest request, CancellationToken ct = default)
    {
        var g = await _guardians.GetByIdAsync(id, ct);
        if (g is null) return Result.Failure("Guardian not found.");

        if (await _guardians.EmailExistsAsync(request.Email, id, ct))
            return Result.Failure("Email already in use by another guardian.");

        var contactNumber = ContactNumber.Normalize(request.ContactNumber);
        if (contactNumber is null)
            return Result.Failure("A valid contact number is required.");
        if (contactNumber != g.Phone && await _guardians.ContactNumberExistsAsync(contactNumber, ct))
            return Result.Failure("Contact number already in use by another guardian.");

        g.FullName = request.FullName;
        g.Email = request.Email;
        g.Phone = contactNumber;
        g.Status = request.Status;
        g.UpdatedAt = DateTime.UtcNow;
        await _guardians.SaveChangesAsync(ct);
        return Result.Success();
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

        // The contact number is the Guardian login identifier, so it is normalized
        // on write and must stay unique across accounts.
        var contactNumber = ContactNumber.Normalize(request.Phone);
        if (contactNumber is null)
            return Result.Failure("A valid contact number is required.");
        if (contactNumber != g.Phone && await _guardians.ContactNumberExistsAsync(contactNumber, ct))
            return Result.Failure("Contact number already in use by another guardian.");

        g.FullName = request.FullName;
        g.Phone = contactNumber;
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

    public async Task<Result> AdminUpdateCredentialsAsync(int id, AdminUpdateGuardianCredentialsRequest request, CancellationToken ct = default)
    {
        var g = await _guardians.GetByIdAsync(id, ct);
        if (g is null) return Result.Failure("Guardian not found.");

        // An admin-supplied password is a default: the Guardian must replace it at
        // next login before any session is issued.
        g.PasswordHash = _hasher.Hash(request.NewPassword);
        g.PasswordResetRequired = true;
        g.UpdatedAt = DateTime.UtcNow;
        // A password the admin chose resets trust: every phone must pass the OTP again.
        await _guardians.RevokeTrustedDevicesAsync(g.GuardianId, DateTime.UtcNow, null, ct);
        await _guardians.SaveChangesAsync(ct);
        return Result.Success();
    }
}
