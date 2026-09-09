using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class GuardianService : IGuardianService
{
    private readonly IGuardianRepository _guardians;
    private readonly IPasswordHasherService _hasher;
    private readonly IEmailSender _email;

    public GuardianService(IGuardianRepository guardians, IPasswordHasherService hasher, IEmailSender email)
    {
        _guardians = guardians;
        _hasher = hasher;
        _email = email;
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

        var guardian = new Guardian
        {
            FullName = request.FullName,
            Email = request.Email,
            Phone = contactNumber,
            // Hashed before it ever reaches the database; the plaintext exists only
            // in this method and in the email sent to the Guardian.
            PasswordHash = _hasher.Hash(GuardianCredentialDefaults.InitialPassword),
            PasswordResetRequired = true,
            Status = RecordStatus.Active
        };

        await _guardians.AddAsync(guardian, ct);
        await _guardians.SaveChangesAsync(ct);

        // Sent to the Guardian's own registered address, never the admin's.
        var message = GuardianEmails.BuildCredentialEmail(
            guardian.FullName, guardian.Email, contactNumber, GuardianCredentialDefaults.InitialPassword);
        var delivery = await _email.SendAsync(message, ct);

        // A delivery failure does not undo the account, but it is reported to the
        // admin rather than swallowed.
        return Result<CreateGuardianResultDto>.Success(
            new CreateGuardianResultDto(guardian.GuardianId, delivery.Succeeded, delivery.Succeeded ? null : delivery.Error));
    }

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
            g.PasswordResetRequired,
            g.StudentGuardians.Select(sg => new StudentSummaryDto(
                sg.StudentId, sg.Student.FullName, sg.Student.Grade, sg.Relationship, sg.IsAuthorizedForPickup)).ToList(),
            g.CreatedAt));
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
        await _guardians.SaveChangesAsync(ct);
        return Result.Success();
    }
}
