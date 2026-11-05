using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

/// <summary>
/// Pickup verification state machine (see <see cref="GuardianVerification.GetState"/>):
///
///   Pending    face matched, undecided, younger than Verification:PendingExpiryMinutes (15)
///   Approved   terminal; records SchoolDeparture event + attendance in one transaction
///   Rejected   terminal
///   Expired    terminal; pending too long, no decision possible
///   NotMatched face did not match; can only be rejected
///
///   Pending    -> Approved   allowed (guardian + student active, student at school today)
///   Pending    -> Rejected   allowed
///   NotMatched -> Rejected   allowed
///   Approved   -> Approved   blocked ("already approved")
///   Approved   -> Rejected   blocked
///   Rejected   -> Rejected   idempotent success, no second notification
///   Rejected   -> Approved   blocked — a new face verification is required
///   Expired    -> any        blocked — a new face verification is required
///
/// Concurrency: decisions on one verification are guarded by its Version token, so a
/// stale read can never apply a second decision; the loser gets DecidedConcurrentlyMessage.
/// Approvals also hold the student journey lock, so two approvals of different
/// verifications for one child cannot both record a departure.
/// </summary>
public class VerificationService : IVerificationService
{
    public const string DecidedConcurrentlyMessage =
        "This pickup was just decided by another admin. Reload to see the outcome.";
    public const string RejectedCannotBeApprovedMessage =
        "This pickup was rejected and cannot be approved. Start a new face verification.";
    public const string ExpiredMessage =
        "This verification has expired. Start a new face verification.";

    private readonly IVerificationRepository _verifications;
    private readonly IStudentRepository _students;
    private readonly IGuardianRepository _guardians;
    private readonly IJourneyRepository _journey;
    private readonly INotificationRepository _notifications;
    private readonly IPushNotificationSender _pushSender;
    private readonly IFaceServiceClient _faceClient;
    private readonly JourneyValidationService _validation;
    private readonly SchoolClock _clock;
    private readonly VerificationOptions _options;

    public VerificationService(
        IVerificationRepository verifications,
        IStudentRepository students,
        IGuardianRepository guardians,
        IJourneyRepository journey,
        INotificationRepository notifications,
        IPushNotificationSender pushSender,
        IFaceServiceClient faceClient,
        JourneyValidationService validation,
        SchoolClock clock,
        VerificationOptions options)
    {
        _verifications = verifications;
        _students = students;
        _guardians = guardians;
        _journey = journey;
        _notifications = notifications;
        _pushSender = pushSender;
        _faceClient = faceClient;
        _validation = validation;
        _clock = clock;
        _options = options;
    }

    public async Task<Result> RegisterFaceAsync(int guardianId, RegisterFaceRequest request, CancellationToken ct = default)
    {
        var guardian = await _guardians.GetByIdAsync(guardianId, ct);
        if (guardian is null) return Result.Failure("Guardian not found.");

        var embeddingResult = await _faceClient.RegisterFaceAsync(request.ImageBase64, ct);
        if (!embeddingResult.Succeeded)
            return Result.Failure(embeddingResult.Error ?? "Face registration failed.");

        await _verifications.UpsertFaceDataAsync(new GuardianFaceData
        {
            GuardianId = guardianId,
            EmbeddingJson = embeddingResult.Value!.EmbeddingJson,
            Model = embeddingResult.Value.Model,
            IsActive = true,
            RegisteredAt = _clock.UtcNow
        }, ct);
        await _verifications.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<FaceVerifyResultDto>> VerifyAsync(FaceVerifyRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(request.StudentId, ct);
        if (student is null) return Result<FaceVerifyResultDto>.Failure("Student not found.");

        var link = await _students.GetStudentGuardianAsync(request.StudentId, request.GuardianId, ct);
        if (link is null || !link.IsAuthorizedForPickup)
            return Result<FaceVerifyResultDto>.Success(new FaceVerifyResultDto(
                0, false, false, 0, "Guardian is not authorized to pick up this student."));

        var faceData = await _verifications.GetFaceDataAsync(request.GuardianId, ct);
        if (faceData is null || !faceData.IsActive)
            return Result<FaceVerifyResultDto>.Success(new FaceVerifyResultDto(
                0, true, false, 0, "Guardian has no registered face data."));

        var matchResult = await _faceClient.VerifyFaceAsync(faceData.EmbeddingJson, request.ImageBase64, ct);
        if (!matchResult.Succeeded)
            return Result<FaceVerifyResultDto>.Failure(matchResult.Error ?? "Face verification service error.");

        var verification = new GuardianVerification
        {
            StudentId = request.StudentId,
            GuardianId = request.GuardianId,
            AttemptedAt = _clock.UtcNow,
            MatchScore = matchResult.Value!.Score,
            Result = matchResult.Value.Match ? VerificationResult.Match : VerificationResult.NoMatch,
            PickupApproved = false
        };
        await _verifications.AddVerificationAsync(verification, ct);
        await _verifications.SaveChangesAsync(ct);

        var message = matchResult.Value.Match
            ? "Face verified. Awaiting pickup approval."
            : "Face verification failed — face does not match registered guardian.";

        return Result<FaceVerifyResultDto>.Success(new FaceVerifyResultDto(
            verification.Id, true, matchResult.Value.Match, matchResult.Value.Score, message,
            matchResult.Value.Match ? verification.AttemptedAt + _options.PendingExpiry : null));
    }

    public async Task<Result<PickupDecisionResultDto>> ApproveAsync(ApprovePickupRequest request, int approvingAdminId, CancellationToken ct = default)
    {
        var verification = await _verifications.GetVerificationByIdAsync(request.VerificationId, ct);
        if (verification is null) return Result<PickupDecisionResultDto>.Failure("Verification record not found.");

        var blocked = ApprovalBlockedReason(verification.GetState(_clock.UtcNow, _options.PendingExpiry));
        if (blocked is not null) return Result<PickupDecisionResultDto>.Failure(blocked);

        // The time between face verification and an admin's approval is the
        // whole point of the pending queue, so both parties' status could have
        // changed since VerifyAsync ran — re-check right before acting on it.
        if (verification.Guardian.Status != RecordStatus.Active)
            return Result<PickupDecisionResultDto>.Failure("This guardian's account is no longer active.");
        if (verification.Student.Status != RecordStatus.Active)
            return Result<PickupDecisionResultDto>.Failure("This student's record is no longer active.");

        // A guardian pickup only makes sense while the student is currently at school —
        // it produces a School Departure event. (Bus Drop-off is the bus's own checkpoint,
        // not a face-verified personal pickup, so it is not a valid pickup target here.)
        const JourneyEventType next = JourneyEventType.SchoolDeparture;

        // State check, approval, departure event and departure attendance commit
        // together under the student's journey lock: two approvals of different
        // verifications for one child cannot both see "at school", and an approval is
        // never saved without its departure records (or vice versa).
        var (journeyEvent, failure) = await _journey.RunSerializedForStudentAsync(verification.StudentId, async lockCt =>
        {
            var now = _clock.UtcNow;

            // Re-evaluated at the moment of writing: the request may have waited on the lock.
            var stillBlocked = ApprovalBlockedReason(verification.GetState(now, _options.PendingExpiry));
            if (stillBlocked is not null)
                return ((JourneyEvent?)null, stillBlocked);

            var lastEvent = await _journey.GetLastEventSinceAsync(verification.StudentId, _clock.TodayStartUtc, lockCt);
            var stateCheck = _validation.ValidateNextEvent(lastEvent?.EventType, next);
            if (!stateCheck.Succeeded)
                return (null, $"Pickup cannot be recorded: {stateCheck.Error}");

            verification.PickupApproved = true;
            verification.ApprovedByAdminId = approvingAdminId;
            verification.Version++;
            try
            {
                // Version token: fails if any other decision landed since we read the row.
                await _verifications.SaveChangesAsync(lockCt);
            }
            catch (ConcurrencyConflictException)
            {
                return (null, DecidedConcurrentlyMessage);
            }

            var created = new JourneyEvent
            {
                StudentId = verification.StudentId,
                EventType = next,
                Timestamp = now,
                Source = EventSource.Manual
            };
            await _journey.AddJourneyEventAsync(created, lockCt);

            // Same reporting rule as an RFID departure: every recorded School
            // Departure has a matching attendance row. A retried approval cannot
            // add a second one — it is refused above as "already approved".
            var (date, time) = _clock.ToSchoolDateTime(now);
            await _journey.AddAttendanceAsync(new Attendance
            {
                StudentId = verification.StudentId,
                EventType = AttendanceEventType.SchoolDeparture,
                RecordedAtUtc = now,
                Date = date,
                Time = time
            }, lockCt);

            await _journey.SaveChangesAsync(lockCt);
            return (created, (string?)null);
        }, ct);

        if (journeyEvent is null)
            return Result<PickupDecisionResultDto>.Failure(failure!);

        var student = await _students.GetByIdAsync(verification.StudentId, ct);
        var approvedTitle = "Pickup approved";
        var approvedMessage = $"{student?.FullName} pickup approved and verified at {_clock.ToLocal(journeyEvent.Timestamp):t}.";
        await _notifications.AddAsync(new Notification
        {
            GuardianId = verification.GuardianId,
            StudentId = verification.StudentId,
            Type = NotificationType.PickupVerification,
            Title = approvedTitle,
            Message = approvedMessage,
            RelatedEntityType = nameof(GuardianVerification),
            RelatedEntityId = verification.Id
        }, ct);
        await _notifications.SaveChangesAsync(ct);
        await _pushSender.SendAsync(approvedTitle, approvedMessage, verification.GuardianId, ct);

        return Result<PickupDecisionResultDto>.Success(new PickupDecisionResultDto(true, "Pickup approved and recorded.", next));
    }

    public async Task<Result<PickupDecisionResultDto>> RejectAsync(RejectPickupRequest request, int actingAdminId, CancellationToken ct = default)
    {
        var verification = await _verifications.GetVerificationByIdAsync(request.VerificationId, ct);
        if (verification is null) return Result<PickupDecisionResultDto>.Failure("Verification record not found.");

        switch (verification.GetState(_clock.UtcNow, _options.PendingExpiry))
        {
            case PickupVerificationState.Approved:
                return Result<PickupDecisionResultDto>.Failure("This pickup was already approved and cannot be rejected.");
            case PickupVerificationState.Rejected:
                // Repeat call: a no-op instead of re-sending the rejection notification.
                return Result<PickupDecisionResultDto>.Success(new PickupDecisionResultDto(false, "Pickup rejected.", null));
            case PickupVerificationState.Expired:
                return Result<PickupDecisionResultDto>.Failure(ExpiredMessage);
        }

        verification.PickupApproved = false;
        verification.ApprovedByAdminId = actingAdminId;
        verification.Notes = request.Reason;
        verification.Version++;
        try
        {
            await _verifications.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            // Another admin decided this verification between our read and write —
            // possibly an approval. Reporting "rejected" here could tell the desk a
            // child was not released when they were, so surface the conflict instead.
            return Result<PickupDecisionResultDto>.Failure(DecidedConcurrentlyMessage);
        }

        var student = await _students.GetByIdAsync(verification.StudentId, ct);
        var rejectedTitle = "Pickup rejected";
        var rejectedMessage = $"{student?.FullName} pickup was rejected." + (request.Reason is null ? "" : $" Reason: {request.Reason}");
        await _notifications.AddAsync(new Notification
        {
            GuardianId = verification.GuardianId,
            StudentId = verification.StudentId,
            Type = NotificationType.PickupVerification,
            Title = rejectedTitle,
            Message = rejectedMessage,
            RelatedEntityType = nameof(GuardianVerification),
            RelatedEntityId = verification.Id
        }, ct);
        await _notifications.SaveChangesAsync(ct);
        await _pushSender.SendAsync(rejectedTitle, rejectedMessage, verification.GuardianId, ct);

        return Result<PickupDecisionResultDto>.Success(new PickupDecisionResultDto(false, "Pickup rejected.", null));
    }

    public async Task<List<GuardianVerificationHistoryDto>> GetHistoryAsync(int? studentId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        DateTime? fromUtc = from is null ? null : _clock.StartOfDayUtc(from.Value);
        DateTime? toUtcExclusive = to is null ? null : _clock.StartOfDayUtc(to.Value.AddDays(1));
        var now = _clock.UtcNow;
        return (await _verifications.GetHistoryAsync(studentId, fromUtc, toUtcExclusive, ct))
            .Select(v => new GuardianVerificationHistoryDto(
                v.Id, v.StudentId, v.Student.FullName, v.GuardianId, v.Guardian.FullName,
                v.AttemptedAt, v.MatchScore, v.Result, v.PickupApproved,
                v.GetState(now, _options.PendingExpiry),
                v.Result == VerificationResult.Match ? v.AttemptedAt + _options.PendingExpiry : null))
            .ToList();
    }

    private static string? ApprovalBlockedReason(PickupVerificationState state) => state switch
    {
        PickupVerificationState.Pending => null,
        PickupVerificationState.Approved => "Pickup already approved for this verification.",
        PickupVerificationState.Rejected => RejectedCannotBeApprovedMessage,
        PickupVerificationState.Expired => ExpiredMessage,
        _ => "Cannot approve pickup — face was not verified as a match."
    };
}
