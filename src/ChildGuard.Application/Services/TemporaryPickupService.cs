using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

/// <summary>
/// Temporary pickup authorisations (see <see cref="TemporaryPickupAuthorization.GetState"/>):
///
///   PendingApproval -> Approved (admin)  -> Scheduled/Active by the clock
///   PendingApproval -> Rejected (admin) | Cancelled (requesting guardian) | Expired (window closed)
///   Scheduled/Active -> Revoked (admin) | Cancelled (requesting guardian) | Expired (window closed)
///   Active           -> Completed (desk handover: valid QR + face match + child at school)
///   Completed, Rejected, Cancelled, Revoked, Expired are terminal.
///
/// Who: only a guardian linked to the student AND authorised for pickup may request; only the requester may
/// cancel; only admins decide and hand over. Safety: the window is at most 24 hours; requests for the same
/// child may not overlap; the handover runs under the student's journey lock and records the School Departure
/// event and attendance in the same transaction, so a child can never be handed over twice or after leaving.
/// Every step, including refused scans and failed face checks, is written to the audit trail.
/// </summary>
public class TemporaryPickupService : ITemporaryPickupService
{
    public const string NotYourStudentMessage = "This student is not linked to your account.";
    public const string NotFoundMessage = "Temporary pickup not found.";
    public const string ChangedConcurrentlyMessage = "This temporary pickup was just changed by someone else. Reload and try again.";
    public const string InvalidQrMessage = "This QR code is not valid. It may have been revoked, cancelled or replaced.";
    public const string FaceMismatchMessage = "The face does not match the photo the guardian supplied. Do not release the child.";

    private const string RoleGuardian = "Guardian";
    private const string RoleAdmin = "Admin";

    private readonly ITemporaryPickupRepository _repo;
    private readonly IStudentRepository _students;
    private readonly IGuardianRepository _guardians;
    private readonly IJourneyRepository _journey;
    private readonly INotificationRepository _notifications;
    private readonly IPushNotificationSender _push;
    private readonly IFaceServiceClient _face;
    private readonly IEmbeddingProtector _protector;
    private readonly ITemporaryPickupTokenService _tokens;
    private readonly JourneyValidationService _validation;
    private readonly SchoolClock _clock;
    private readonly TemporaryPickupOptions _options;

    public TemporaryPickupService(
        ITemporaryPickupRepository repo,
        IStudentRepository students,
        IGuardianRepository guardians,
        IJourneyRepository journey,
        INotificationRepository notifications,
        IPushNotificationSender push,
        IFaceServiceClient face,
        IEmbeddingProtector protector,
        ITemporaryPickupTokenService tokens,
        JourneyValidationService validation,
        SchoolClock clock,
        TemporaryPickupOptions options)
    {
        _repo = repo;
        _students = students;
        _guardians = guardians;
        _journey = journey;
        _notifications = notifications;
        _push = push;
        _face = face;
        _protector = protector;
        _tokens = tokens;
        _validation = validation;
        _clock = clock;
        _options = options;
    }

    // ------------------------------------------------------------------ guardian

    public async Task<Result<TemporaryPickupDto>> CreateAsync(int guardianId, CreateTemporaryPickupRequest request, CancellationToken ct = default)
    {
        var link = await _students.GetStudentGuardianAsync(request.StudentId, guardianId, ct);
        if (link is null) return Result<TemporaryPickupDto>.Failure(NotYourStudentMessage);
        if (!link.IsAuthorizedForPickup)
            return Result<TemporaryPickupDto>.Failure("Only a guardian who is authorised to collect this child can arrange a temporary pickup.");

        var guardian = await _guardians.GetByIdAsync(guardianId, ct);
        var student = await _students.GetByIdAsync(request.StudentId, ct);
        if (guardian is null || guardian.Status != RecordStatus.Active)
            return Result<TemporaryPickupDto>.Failure("Your account is not active.");
        if (student is null || student.Status != RecordStatus.Active)
            return Result<TemporaryPickupDto>.Failure("This student's record is not active.");

        var now = _clock.UtcNow;
        var from = DateTime.SpecifyKind(request.ValidFromUtc.ToUniversalTime(), DateTimeKind.Utc);
        var until = DateTime.SpecifyKind(request.ValidUntilUtc.ToUniversalTime(), DateTimeKind.Utc);
        var windowError = CheckWindow(from, until, now);
        if (windowError is not null) return Result<TemporaryPickupDto>.Failure(windowError);

        if (await _repo.HasOverlappingOpenAsync(request.StudentId, from, until, ct))
            return Result<TemporaryPickupDto>.Failure(
                "Another temporary pickup for this child already covers part of that time. Cancel it first or choose another time.");

        var bytes = DecodeBase64(request.PhotoBase64);
        if (bytes is null) return Result<TemporaryPickupDto>.Failure("The photo could not be read. Choose the photo again.");
        var type = ProfileImage.Validate(bytes);
        if (!type.Succeeded) return Result<TemporaryPickupDto>.Failure(type.Error!);

        // The face service rejects photos without exactly one usable face, so a blank or group photo never gets through.
        var embedding = await _face.RegisterFaceAsync(request.PhotoBase64, ct);
        if (!embedding.Succeeded)
            return Result<TemporaryPickupDto>.Failure(
                $"The photo must show one person's face clearly, looking at the camera. {embedding.Error}".Trim());

        var auth = new TemporaryPickupAuthorization
        {
            StudentId = request.StudentId,
            RequestedByGuardianId = guardianId,
            FullName = request.FullName.Trim(),
            Phone = ContactNumber.Normalize(request.Phone) ?? request.Phone.Trim(),
            Relationship = request.Relationship.Trim(),
            IdentityNote = string.IsNullOrWhiteSpace(request.IdentityNote) ? null : request.IdentityNote.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ValidFromUtc = from,
            ValidUntilUtc = until,
            Status = TemporaryPickupStatus.Pending,
            CreatedAt = now,
            FaceEmbeddingJson = "",
            FaceModel = embedding.Value!.Model,
            Photo = new TemporaryPickupPhoto { ContentType = type.Value!, Data = bytes, ByteSize = bytes.Length, UpdatedAt = now }
        };
        await _repo.AddAsync(auth, ct);
        await _repo.SaveChangesAsync(ct);

        // The protector authenticates the owner id, which only exists once the row is saved.
        auth.FaceEmbeddingJson = _protector.Protect(embedding.Value.EmbeddingJson, auth.ProtectionOwnerId);
        await AuditAsync(auth, "Requested", RoleGuardian, guardianId, $"window={from:O}..{until:O}", ct);
        await _repo.SaveChangesAsync(ct);

        await NotifyAsync([guardianId], auth.StudentId, auth.Id, "Temporary pickup requested",
            $"Your request for {auth.FullName} to collect {student.FullName} is waiting for school approval.", ct);

        return Result<TemporaryPickupDto>.Success(ToDto(auth, guardianId, hasPhoto: true, now));
    }

    public async Task<List<TemporaryPickupDto>> ListForGuardianAsync(int guardianId, CancellationToken ct = default)
    {
        var studentIds = (await _guardians.GetWithStudentsAsync(guardianId, ct))?.StudentGuardians.Select(sg => sg.StudentId).ToList()
                         ?? new List<int>();
        if (studentIds.Count == 0) return new List<TemporaryPickupDto>();
        var rows = await _repo.ListAsync(null, studentIds, _clock.UtcNow.AddDays(-90), ct);
        return await ToDtosAsync(rows, guardianId, ct);
    }

    public async Task<Result<TemporaryPickupDetailDto>> GetForGuardianAsync(int guardianId, int id, CancellationToken ct = default)
    {
        var auth = await LoadForGuardianAsync(guardianId, id, ct);
        if (auth is null) return Result<TemporaryPickupDetailDto>.Failure(NotFoundMessage);
        return Result<TemporaryPickupDetailDto>.Success(await DetailAsync(auth, guardianId, ct));
    }

    public async Task<Result<TemporaryPickupDto>> CancelAsync(int guardianId, int id, string? reason, CancellationToken ct = default)
    {
        var auth = await LoadForGuardianAsync(guardianId, id, ct);
        if (auth is null) return Result<TemporaryPickupDto>.Failure(NotFoundMessage);
        if (auth.RequestedByGuardianId != guardianId)
            return Result<TemporaryPickupDto>.Failure("Only the guardian who made this request can cancel it.");

        var now = _clock.UtcNow;
        var state = auth.GetState(now);
        if (state == TemporaryPickupState.Cancelled)
            return Result<TemporaryPickupDto>.Success(ToDto(auth, guardianId, auth.Photo is not null, now));
        if (state is not (TemporaryPickupState.PendingApproval or TemporaryPickupState.Scheduled or TemporaryPickupState.Active))
            return Result<TemporaryPickupDto>.Failure($"This temporary pickup is {Describe(state)} and can no longer be cancelled.");

        auth.Status = TemporaryPickupStatus.Cancelled;
        auth.CancelledAt = now;
        auth.QrNonce = null;
        auth.DecisionNote = Trim(reason, 500) ?? auth.DecisionNote;
        var saved = await SaveStateAsync(auth, "Cancelled", RoleGuardian, guardianId, Trim(reason, 200), ct);
        if (!saved.Succeeded) return Result<TemporaryPickupDto>.Failure(saved.Error!);

        await NotifyLinkedAsync(auth, "Temporary pickup cancelled",
            $"The temporary pickup by {auth.FullName} for {auth.Student.FullName} was cancelled.", ct);
        return Result<TemporaryPickupDto>.Success(ToDto(auth, guardianId, auth.Photo is not null, now));
    }

    public async Task<Result<TemporaryPickupQrDto>> GetQrAsync(int guardianId, int id, CancellationToken ct = default)
    {
        var auth = await LoadForGuardianAsync(guardianId, id, ct);
        if (auth is null) return Result<TemporaryPickupQrDto>.Failure(NotFoundMessage);
        if (auth.RequestedByGuardianId != guardianId)
            return Result<TemporaryPickupQrDto>.Failure("Only the guardian who made this request can share its QR code.");

        var state = auth.GetState(_clock.UtcNow);
        if (state is not (TemporaryPickupState.Scheduled or TemporaryPickupState.Active) || auth.QrNonce is null)
            return Result<TemporaryPickupQrDto>.Failure(state == TemporaryPickupState.PendingApproval
                ? "The QR code is issued once the school approves this request."
                : $"This temporary pickup is {Describe(state)}, so it has no valid QR code.");

        return Result<TemporaryPickupQrDto>.Success(new TemporaryPickupQrDto(
            auth.Id, _tokens.CreateToken(auth.Id, auth.QrNonce), auth.ValidFromUtc, auth.ValidUntilUtc));
    }

    public async Task<Result<PhotoContentDto>> GetPhotoForGuardianAsync(int guardianId, int id, CancellationToken ct = default)
    {
        var auth = await LoadForGuardianAsync(guardianId, id, ct);
        return auth is null ? Result<PhotoContentDto>.Failure(NotFoundMessage) : await GetPhotoAsync(id, ct);
    }

    // ------------------------------------------------------------------ admin

    public async Task<List<TemporaryPickupDto>> ListForAdminAsync(TemporaryPickupState? state, int? studentId, CancellationToken ct = default)
    {
        var rows = await _repo.ListAsync(studentId, null, _clock.UtcNow.AddDays(-90), ct);
        var dtos = await ToDtosAsync(rows, null, ct);
        return state is null ? dtos : dtos.Where(d => d.State == state).ToList();
    }

    public async Task<TemporaryPickupSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var rows = await _repo.ListAsync(null, null, now.AddDays(-(_options.MaxAdvanceDays + 2)), ct);
        var today = _clock.TodayStartUtc;
        return new TemporaryPickupSummaryDto(
            rows.Count(r => r.GetState(now) == TemporaryPickupState.PendingApproval),
            rows.Count(r => r.GetState(now) == TemporaryPickupState.Active),
            rows.Count(r => r.GetState(now) == TemporaryPickupState.Scheduled),
            rows.Count(r => r.CompletedAt >= today));
    }

    public async Task<Result<TemporaryPickupDetailDto>> GetForAdminAsync(int id, CancellationToken ct = default)
    {
        var auth = await _repo.GetAsync(id, ct);
        return auth is null
            ? Result<TemporaryPickupDetailDto>.Failure(NotFoundMessage)
            : Result<TemporaryPickupDetailDto>.Success(await DetailAsync(auth, null, ct));
    }

    public async Task<Result<PhotoContentDto>> GetPhotoAsync(int id, CancellationToken ct = default)
    {
        var photo = await _repo.GetPhotoAsync(id, ct);
        return photo is null
            ? Result<PhotoContentDto>.Failure("No photo on file.")
            : Result<PhotoContentDto>.Success(new PhotoContentDto(photo.Data, photo.ContentType, photo.UpdatedAt));
    }

    public async Task<Result<TemporaryPickupDto>> ApproveAsync(int adminId, int id, CancellationToken ct = default)
    {
        var auth = await _repo.GetAsync(id, ct);
        if (auth is null) return Result<TemporaryPickupDto>.Failure(NotFoundMessage);

        var now = _clock.UtcNow;
        var state = auth.GetState(now);
        if (state != TemporaryPickupState.PendingApproval)
            return Result<TemporaryPickupDto>.Failure($"This request is {Describe(state)} and cannot be approved.");

        // Everything may have changed since the guardian asked: re-check before granting anything.
        if (auth.Student.Status != RecordStatus.Active)
            return Result<TemporaryPickupDto>.Failure("This student's record is no longer active.");
        var link = await _students.GetStudentGuardianAsync(auth.StudentId, auth.RequestedByGuardianId, ct);
        if (link is null || !link.IsAuthorizedForPickup || auth.RequestedByGuardian.Status != RecordStatus.Active)
            return Result<TemporaryPickupDto>.Failure("The requesting guardian is no longer an active, pickup-authorised guardian of this student.");
        if (string.IsNullOrEmpty(auth.FaceEmbeddingJson) || auth.Photo is null)
            return Result<TemporaryPickupDto>.Failure("This request has no usable photo, so it cannot be approved. Ask the guardian to submit a new request.");

        auth.Status = TemporaryPickupStatus.Approved;
        auth.DecidedByAdminId = adminId;
        auth.DecidedAt = now;
        auth.QrNonce = _tokens.NewNonce();
        var saved = await SaveStateAsync(auth, "Approved", RoleAdmin, adminId, null, ct);
        if (!saved.Succeeded) return Result<TemporaryPickupDto>.Failure(saved.Error!);

        var window = $"{_clock.ToLocal(auth.ValidFromUtc):d MMM, h:mm tt} – {_clock.ToLocal(auth.ValidUntilUtc):d MMM, h:mm tt}";
        await NotifyAsync([auth.RequestedByGuardianId], auth.StudentId, auth.Id, "Temporary pickup approved",
            $"{auth.FullName} may collect {auth.Student.FullName} ({window}). Open the app to share the pickup QR code.", ct);
        await NotifyOthersAsync(auth, "Temporary pickup arranged",
            $"{auth.RequestedByGuardian.FullName} arranged for {auth.FullName} to collect {auth.Student.FullName} ({window}).", ct);
        return Result<TemporaryPickupDto>.Success(ToDto(auth, null, true, now));
    }

    public async Task<Result<TemporaryPickupDto>> RejectAsync(int adminId, int id, string? reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result<TemporaryPickupDto>.Failure("Give a reason so the guardian knows what to change.");
        var auth = await _repo.GetAsync(id, ct);
        if (auth is null) return Result<TemporaryPickupDto>.Failure(NotFoundMessage);

        var now = _clock.UtcNow;
        var state = auth.GetState(now);
        if (state == TemporaryPickupState.Rejected) return Result<TemporaryPickupDto>.Success(ToDto(auth, null, auth.Photo is not null, now));
        if (state != TemporaryPickupState.PendingApproval)
            return Result<TemporaryPickupDto>.Failure($"This request is {Describe(state)} and cannot be rejected.");

        auth.Status = TemporaryPickupStatus.Rejected;
        auth.DecidedByAdminId = adminId;
        auth.DecidedAt = now;
        auth.DecisionNote = Trim(reason, 500);
        auth.QrNonce = null;
        var saved = await SaveStateAsync(auth, "Rejected", RoleAdmin, adminId, Trim(reason, 200), ct);
        if (!saved.Succeeded) return Result<TemporaryPickupDto>.Failure(saved.Error!);

        await NotifyAsync([auth.RequestedByGuardianId], auth.StudentId, auth.Id, "Temporary pickup not approved",
            $"Your request for {auth.FullName} to collect {auth.Student.FullName} was not approved. Reason: {auth.DecisionNote}", ct);
        return Result<TemporaryPickupDto>.Success(ToDto(auth, null, auth.Photo is not null, now));
    }

    public async Task<Result<TemporaryPickupDto>> RevokeAsync(int adminId, int id, string? reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result<TemporaryPickupDto>.Failure("Give a reason for revoking this pickup.");
        var auth = await _repo.GetAsync(id, ct);
        if (auth is null) return Result<TemporaryPickupDto>.Failure(NotFoundMessage);

        var now = _clock.UtcNow;
        var state = auth.GetState(now);
        if (state == TemporaryPickupState.Revoked) return Result<TemporaryPickupDto>.Success(ToDto(auth, null, auth.Photo is not null, now));
        if (state is not (TemporaryPickupState.Scheduled or TemporaryPickupState.Active))
            return Result<TemporaryPickupDto>.Failure($"This temporary pickup is {Describe(state)} and cannot be revoked.");

        auth.Status = TemporaryPickupStatus.Revoked;
        auth.DecidedByAdminId = adminId;
        auth.DecidedAt = now;
        auth.DecisionNote = Trim(reason, 500);
        auth.QrNonce = null; // every shared copy of the QR stops working at once
        var saved = await SaveStateAsync(auth, "Revoked", RoleAdmin, adminId, Trim(reason, 200), ct);
        if (!saved.Succeeded) return Result<TemporaryPickupDto>.Failure(saved.Error!);

        await NotifyLinkedAsync(auth, "Temporary pickup revoked",
            $"The school revoked the temporary pickup by {auth.FullName} for {auth.Student.FullName}. Reason: {auth.DecisionNote}", ct);
        return Result<TemporaryPickupDto>.Success(ToDto(auth, null, auth.Photo is not null, now));
    }

    // ------------------------------------------------------------------ desk

    public async Task<Result<TemporaryPickupScanResultDto>> ScanAsync(int adminId, string token, CancellationToken ct = default)
    {
        var (auth, error) = await ResolveTokenAsync(adminId, token, ct);
        if (auth is null) return Result<TemporaryPickupScanResultDto>.Failure(error!);

        var now = _clock.UtcNow;
        var (status, blocked) = await HandoverBlockedReasonAsync(auth, now, ct);
        await AuditAsync(auth, "QrScanned", RoleAdmin, adminId, blocked is null ? "ready" : $"blocked: {blocked}", ct);
        await _repo.SaveChangesAsync(ct);

        return Result<TemporaryPickupScanResultDto>.Success(new TemporaryPickupScanResultDto(
            ToDto(auth, null, auth.Photo is not null, now), blocked is null, blocked, status));
    }

    public async Task<Result<TemporaryPickupHandoverResultDto>> HandOverAsync(
        int adminId, int id, TemporaryPickupHandoverRequest request, CancellationToken ct = default)
    {
        var (auth, error) = await ResolveTokenAsync(adminId, request.Token, ct);
        if (auth is null) return Result<TemporaryPickupHandoverResultDto>.Failure(error!);
        if (auth.Id != id) return Result<TemporaryPickupHandoverResultDto>.Failure(InvalidQrMessage);

        var (_, blocked) = await HandoverBlockedReasonAsync(auth, _clock.UtcNow, ct);
        if (blocked is not null)
        {
            await AuditAsync(auth, "HandoverRefused", RoleAdmin, adminId, blocked, ct);
            await _repo.SaveChangesAsync(ct);
            return Result<TemporaryPickupHandoverResultDto>.Failure(blocked);
        }

        // Second factor: the person at the desk must match the photo the guardian supplied.
        string embedding;
        try
        {
            embedding = _protector.Unprotect(auth.FaceEmbeddingJson, auth.ProtectionOwnerId);
        }
        catch (EmbeddingProtectionException)
        {
            return Result<TemporaryPickupHandoverResultDto>.Failure("The stored face data for this pickup could not be read. Do not release the child; contact the guardian.");
        }

        var match = await _face.VerifyFaceAsync(embedding, request.ImageBase64, ct);
        if (!match.Succeeded)
            return Result<TemporaryPickupHandoverResultDto>.Failure(match.Error ?? "The face check could not be completed. Try again.");

        var score = Math.Round(match.Value!.Score, 4);
        if (!match.Value.Match)
        {
            await AuditAsync(auth, "FaceMismatch", RoleAdmin, adminId, $"score={score}", ct);
            await _repo.SaveChangesAsync(ct);
            await NotifyAsync([auth.RequestedByGuardianId], auth.StudentId, auth.Id, "Temporary pickup attempt refused",
                $"Someone presented the pickup code for {auth.Student.FullName}, but their face did not match {auth.FullName}'s photo. The child was not released.", ct);
            return Result<TemporaryPickupHandoverResultDto>.Success(new TemporaryPickupHandoverResultDto(false, false, score, FaceMismatchMessage));
        }

        // Handover, departure event and attendance commit together under the student's journey lock.
        var (journeyEvent, failure) = await _journey.RunSerializedForStudentAsync(auth.StudentId, async lockCt =>
        {
            var now = _clock.UtcNow;
            var (_, stillBlocked) = await HandoverBlockedReasonAsync(auth, now, lockCt);
            if (stillBlocked is not null) return ((JourneyEvent?)null, stillBlocked);

            auth.Status = TemporaryPickupStatus.Completed;
            auth.CompletedAt = now;
            auth.CompletedByAdminId = adminId;
            auth.HandoverFaceScore = score;
            auth.QrNonce = null;
            auth.Version++;
            try
            {
                await _repo.SaveChangesAsync(lockCt);
            }
            catch (ConcurrencyConflictException)
            {
                return (null, ChangedConcurrentlyMessage);
            }

            var created = new JourneyEvent { StudentId = auth.StudentId, EventType = JourneyEventType.SchoolDeparture, Timestamp = now, Source = EventSource.Manual };
            await JourneyTransportSnapshot.StampAsync(_journey, created, lockCt);
            await _journey.AddJourneyEventAsync(created, lockCt);
            var (date, time) = _clock.ToSchoolDateTime(now);
            await _journey.AddAttendanceAsync(new Attendance
            {
                StudentId = auth.StudentId,
                EventType = AttendanceEventType.SchoolDeparture,
                RecordedAtUtc = now,
                Date = date,
                Time = time
            }, lockCt);
            await _journey.SaveChangesAsync(lockCt);

            auth.JourneyEventId = created.JourneyEventId;
            await AuditAsync(auth, "HandedOver", RoleAdmin, adminId, $"score={score}; journeyEvent={created.JourneyEventId}", lockCt);
            await _repo.SaveChangesAsync(lockCt);
            return (created, (string?)null);
        }, ct);

        if (journeyEvent is null)
        {
            await AuditAsync(auth, "HandoverRefused", RoleAdmin, adminId, failure, ct);
            await _repo.SaveChangesAsync(ct);
            return Result<TemporaryPickupHandoverResultDto>.Failure(failure!);
        }

        await NotifyLinkedAsync(auth, "Picked up by temporary guardian",
            $"{auth.Student.FullName} was collected by {auth.FullName} at {_clock.ToLocal(journeyEvent.Timestamp):h:mm tt} after a QR and face check.", ct);
        return Result<TemporaryPickupHandoverResultDto>.Success(new TemporaryPickupHandoverResultDto(
            true, true, score, $"Handover recorded. {auth.Student.FullName} has left school with {auth.FullName}."));
    }

    // ------------------------------------------------------------------ helpers

    private string? CheckWindow(DateTime from, DateTime until, DateTime now)
    {
        if (until <= from) return "The end time must be after the start time.";
        if (until - from > _options.MaxWindow)
            return $"A temporary pickup can cover at most {(int)_options.MaxWindow.TotalHours} hours.";
        if (from < now.AddMinutes(-_options.StartGraceMinutes)) return "The start time is in the past.";
        if (until <= now) return "The end time is in the past.";
        if (from > now.AddDays(_options.MaxAdvanceDays))
            return $"Requests can be made at most {_options.MaxAdvanceDays} days ahead.";
        return null;
    }

    /// <summary>Why a handover cannot happen now (null when it can), plus the child's current position.</summary>
    private async Task<(StudentSafetyStatus Status, string? Reason)> HandoverBlockedReasonAsync(
        TemporaryPickupAuthorization auth, DateTime now, CancellationToken ct)
    {
        var last = await _journey.GetLastEventSinceAsync(auth.StudentId, _clock.TodayStartUtc, ct);
        var position = last?.EventType switch
        {
            JourneyEventType.BusBoarding => StudentSafetyStatus.OnBusToSchool,
            JourneyEventType.SchoolArrival => StudentSafetyStatus.AtSchool,
            JourneyEventType.SchoolDeparture => StudentSafetyStatus.LeftSchool,
            JourneyEventType.BusDropoff => StudentSafetyStatus.DroppedOffHome,
            _ => StudentSafetyStatus.NoActivityToday
        };

        var state = auth.GetState(now);
        string? reason = state switch
        {
            TemporaryPickupState.Active => null,
            TemporaryPickupState.Scheduled => $"This pickup is approved from {_clock.ToLocal(auth.ValidFromUtc):d MMM, h:mm tt}, not yet.",
            TemporaryPickupState.PendingApproval => "This request has not been approved by the school.",
            _ => $"This temporary pickup is {Describe(state)}."
        };
        if (reason is null && auth.Student.Status != RecordStatus.Active) reason = "This student's record is not active.";
        if (reason is null)
        {
            var link = await _students.GetStudentGuardianAsync(auth.StudentId, auth.RequestedByGuardianId, ct);
            if (link is null || !link.IsAuthorizedForPickup || auth.RequestedByGuardian.Status != RecordStatus.Active)
                reason = "The guardian who arranged this pickup is no longer authorised. Do not release the child.";
        }
        if (reason is null)
        {
            var next = _validation.ValidateNextEvent(last?.EventType, JourneyEventType.SchoolDeparture);
            if (!next.Succeeded)
                reason = position == StudentSafetyStatus.AtSchool
                    ? $"Pickup cannot be recorded: {next.Error}"
                    : "The child is not checked in at school right now, so there is nobody to hand over.";
        }
        return (position, reason);
    }

    /// <summary>Reads and verifies a QR token. Unknown ids and forged or stale tokens are refused (and audited when the id is real).</summary>
    private async Task<(TemporaryPickupAuthorization? Auth, string? Error)> ResolveTokenAsync(int adminId, string token, CancellationToken ct)
    {
        var id = _tokens.ReadId(token ?? "");
        if (id is null) return (null, "This is not a ChildGuard temporary pickup QR code.");
        var auth = await _repo.GetAsync(id.Value, ct);
        if (auth is null) return (null, InvalidQrMessage);
        if (!_tokens.Verify(token!, auth.Id, auth.QrNonce))
        {
            await AuditAsync(auth, "QrRejected", RoleAdmin, adminId, $"state={auth.GetState(_clock.UtcNow)}", ct);
            await _repo.SaveChangesAsync(ct);
            return (null, InvalidQrMessage);
        }
        return (auth, null);
    }

    private async Task<TemporaryPickupAuthorization?> LoadForGuardianAsync(int guardianId, int id, CancellationToken ct)
    {
        var auth = await _repo.GetAsync(id, ct);
        if (auth is null) return null;
        // Visible to every guardian of the child (co-guardians should know who may collect them).
        return await _students.GetStudentGuardianAsync(auth.StudentId, guardianId, ct) is null ? null : auth;
    }

    private async Task<Result> SaveStateAsync(TemporaryPickupAuthorization auth, string action, string role, int actorId, string? detail, CancellationToken ct)
    {
        auth.Version++;
        await AuditAsync(auth, action, role, actorId, detail, ct);
        try
        {
            await _repo.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure(ChangedConcurrentlyMessage);
        }
    }

    private Task AuditAsync(TemporaryPickupAuthorization auth, string action, string role, int? actorId, string? detail, CancellationToken ct) =>
        _repo.AddAuditAsync(new TemporaryPickupAuditEntry
        {
            AuthorizationId = auth.Id,
            StudentId = auth.StudentId,
            OccurredAt = _clock.UtcNow,
            Action = action,
            ActorRole = role,
            ActorId = actorId,
            Detail = Trim(detail, 500)
        }, ct);

    private async Task NotifyLinkedAsync(TemporaryPickupAuthorization auth, string title, string message, CancellationToken ct)
    {
        var ids = (await _students.GetLinkedGuardiansAsync(auth.StudentId, ct))
            .Where(sg => sg.Guardian.Status == RecordStatus.Active)
            .Select(sg => sg.GuardianId)
            .Append(auth.RequestedByGuardianId)
            .Distinct()
            .ToList();
        await NotifyAsync(ids, auth.StudentId, auth.Id, title, message, ct);
    }

    private async Task NotifyOthersAsync(TemporaryPickupAuthorization auth, string title, string message, CancellationToken ct)
    {
        var ids = (await _students.GetLinkedGuardiansAsync(auth.StudentId, ct))
            .Where(sg => sg.Guardian.Status == RecordStatus.Active && sg.GuardianId != auth.RequestedByGuardianId)
            .Select(sg => sg.GuardianId)
            .ToList();
        await NotifyAsync(ids, auth.StudentId, auth.Id, title, message, ct);
    }

    private async Task NotifyAsync(IReadOnlyCollection<int> guardianIds, int studentId, int authId, string title, string message, CancellationToken ct)
    {
        if (guardianIds.Count == 0) return;
        foreach (var g in guardianIds)
        {
            await _notifications.AddAsync(new Notification
            {
                GuardianId = g,
                StudentId = studentId,
                Type = NotificationType.PickupVerification,
                Title = title,
                Message = message.Length > 1000 ? message[..1000] : message,
                RelatedEntityType = nameof(TemporaryPickupAuthorization),
                RelatedEntityId = authId,
                SentAt = _clock.UtcNow
            }, ct);
        }
        await _notifications.SaveChangesAsync(ct);
        foreach (var g in guardianIds) await _push.SendAsync(title, message, g, ct);
    }

    private async Task<TemporaryPickupDetailDto> DetailAsync(TemporaryPickupAuthorization auth, int? guardianId, CancellationToken ct)
    {
        var audit = (await _repo.GetAuditAsync(auth.Id, ct))
            .Select(a => new TemporaryPickupAuditDto(a.OccurredAt, a.Action, a.ActorRole, a.ActorId, a.Detail))
            .ToList();
        return new TemporaryPickupDetailDto(ToDto(auth, guardianId, auth.Photo is not null, _clock.UtcNow), audit);
    }

    private async Task<List<TemporaryPickupDto>> ToDtosAsync(List<TemporaryPickupAuthorization> rows, int? guardianId, CancellationToken ct)
    {
        var photos = await _repo.GetPhotoIdsAsync(rows.Select(r => r.Id).ToList(), ct);
        var now = _clock.UtcNow;
        return rows.Select(r => ToDto(r, guardianId, photos.Contains(r.Id), now)).ToList();
    }

    private static TemporaryPickupDto ToDto(TemporaryPickupAuthorization a, int? guardianId, bool hasPhoto, DateTime now)
    {
        var state = a.GetState(now);
        var mine = guardianId is not null && a.RequestedByGuardianId == guardianId;
        return new TemporaryPickupDto(
            a.Id, a.StudentId, a.Student?.FullName ?? "", a.Student?.Grade,
            a.RequestedByGuardianId, a.RequestedByGuardian?.FullName ?? "", mine,
            a.FullName, a.Phone, a.Relationship, a.IdentityNote, a.Notes,
            a.ValidFromUtc, a.ValidUntilUtc, a.Status, state, a.CreatedAt,
            a.DecidedByAdmin?.FullName, a.DecidedAt, a.DecisionNote, a.CompletedAt, hasPhoto,
            CanCancel: mine && state is TemporaryPickupState.PendingApproval or TemporaryPickupState.Scheduled or TemporaryPickupState.Active,
            QrAvailable: mine && a.QrNonce is not null && state is TemporaryPickupState.Scheduled or TemporaryPickupState.Active);
    }

    private static string Describe(TemporaryPickupState s) => s switch
    {
        TemporaryPickupState.PendingApproval => "awaiting approval",
        TemporaryPickupState.Scheduled => "approved but not started",
        TemporaryPickupState.Active => "active",
        TemporaryPickupState.Completed => "already completed",
        TemporaryPickupState.Rejected => "rejected",
        TemporaryPickupState.Cancelled => "cancelled",
        TemporaryPickupState.Revoked => "revoked",
        _ => "expired"
    };

    private static string? Trim(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    private static byte[]? DecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var comma = value.IndexOf(',');
        var raw = value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0 ? value[(comma + 1)..] : value;
        try { return Convert.FromBase64String(raw.Trim()); }
        catch (FormatException) { return null; }
    }
}
