using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

/// <summary>
/// Pickup decision state (derived from Result / PickupApproved / ApprovedByAdminId):
///   Pending  = Match, not approved, no deciding admin
///   Approved = PickupApproved (records SchoolDeparture in the same transaction)
///   Rejected = not approved, deciding admin set
///   NoMatch  = face did not match; can only be rejected
/// Transitions enforced here:
///   Pending  -> Approved  allowed (guardian + student active, student at school today)
///   Pending  -> Rejected  allowed
///   Approved -> Rejected  blocked
///   Approved -> Approved  blocked ("already approved")
///   Rejected -> Rejected  idempotent success, no second notification
///   Rejected -> Approved  currently ALLOWED (existing behaviour, pending a business decision)
///   Concurrent decisions on one verification: Version token, loser gets DecidedConcurrentlyMessage.
///   Concurrent approvals of different verifications for one student: serialized by the
///   student journey lock; only the first can record the departure.
/// </summary>
public class VerificationService : IVerificationService
{
    public const string DecidedConcurrentlyMessage =
        "This pickup was just decided by another admin. Reload to see the outcome.";

    private readonly IVerificationRepository _verifications;
    private readonly IStudentRepository _students;
    private readonly IGuardianRepository _guardians;
    private readonly IJourneyRepository _journey;
    private readonly INotificationRepository _notifications;
    private readonly IPushNotificationSender _pushSender;
    private readonly IFaceServiceClient _faceClient;
    private readonly JourneyValidationService _validation;

    public VerificationService(
        IVerificationRepository verifications,
        IStudentRepository students,
        IGuardianRepository guardians,
        IJourneyRepository journey,
        INotificationRepository notifications,
        IPushNotificationSender pushSender,
        IFaceServiceClient faceClient,
        JourneyValidationService validation)
    {
        _verifications = verifications;
        _students = students;
        _guardians = guardians;
        _journey = journey;
        _notifications = notifications;
        _pushSender = pushSender;
        _faceClient = faceClient;
        _validation = validation;
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
            RegisteredAt = DateTime.UtcNow
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
            AttemptedAt = DateTime.UtcNow,
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
            verification.Id, true, matchResult.Value.Match, matchResult.Value.Score, message));
    }

    public async Task<Result<PickupDecisionResultDto>> ApproveAsync(ApprovePickupRequest request, int approvingAdminId, CancellationToken ct = default)
    {
        var verification = await _verifications.GetVerificationByIdAsync(request.VerificationId, ct);
        if (verification is null) return Result<PickupDecisionResultDto>.Failure("Verification record not found.");
        if (verification.Result != VerificationResult.Match)
            return Result<PickupDecisionResultDto>.Failure("Cannot approve pickup — face was not verified as a match.");
        if (verification.PickupApproved)
            return Result<PickupDecisionResultDto>.Failure("Pickup already approved for this verification.");

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

        // State check, approval and departure event happen under the student's
        // journey lock in one transaction: two approvals for different verifications
        // of the same student cannot both see "at school", and an approval can never
        // be saved without its departure event (or vice versa).
        var (journeyEvent, failure) = await _journey.RunSerializedForStudentAsync(verification.StudentId, async lockCt =>
        {
            var now = DateTime.UtcNow;
            var lastEvent = await _journey.GetLastEventSinceAsync(verification.StudentId, SchoolDay.StartUtc(now), lockCt);
            var stateCheck = _validation.ValidateNextEvent(lastEvent?.EventType, next);
            if (!stateCheck.Succeeded)
                return ((JourneyEvent?)null, $"Pickup cannot be recorded: {stateCheck.Error}");

            verification.PickupApproved = true;
            verification.ApprovedByAdminId = approvingAdminId;
            verification.Version++;
            try
            {
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
            await _journey.SaveChangesAsync(lockCt);
            return (created, (string?)null);
        }, ct);

        if (journeyEvent is null)
            return Result<PickupDecisionResultDto>.Failure(failure!);

        var student = await _students.GetByIdAsync(verification.StudentId, ct);
        var approvedTitle = "Pickup approved";
        var approvedMessage = $"{student?.FullName} pickup approved and verified at {journeyEvent.Timestamp:t}.";
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
        if (verification.PickupApproved)
            return Result<PickupDecisionResultDto>.Failure("This pickup was already approved and cannot be rejected.");
        // Already decided (and not approved) means it was rejected before — treat
        // a repeat call as a no-op instead of re-sending the rejection notification.
        if (verification.ApprovedByAdminId is not null)
            return Result<PickupDecisionResultDto>.Success(new PickupDecisionResultDto(false, "Pickup rejected.", null));

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

    public async Task<List<GuardianVerificationHistoryDto>> GetHistoryAsync(int? studentId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default) =>
        (await _verifications.GetHistoryAsync(
            studentId,
            from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            to?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            ct))
            .Select(v => new GuardianVerificationHistoryDto(
                v.Id, v.StudentId, v.Student.FullName, v.GuardianId, v.Guardian.FullName,
                v.AttemptedAt, v.MatchScore, v.Result, v.PickupApproved))
            .ToList();
}
