using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class VerificationService : IVerificationService
{
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

        // A guardian pickup only makes sense while the student is currently at school —
        // it produces a School Departure event. (Bus Drop-off is the bus's own checkpoint,
        // not a face-verified personal pickup, so it is not a valid pickup target here.)
        var lastEvent = await _journey.GetLastEventAsync(verification.StudentId, ct);
        const JourneyEventType next = JourneyEventType.SchoolDeparture;

        var stateCheck = _validation.ValidateNextEvent(lastEvent?.EventType, next);
        if (!stateCheck.Succeeded)
            return Result<PickupDecisionResultDto>.Failure(
                $"Pickup cannot be recorded: {stateCheck.Error}");

        verification.PickupApproved = true;
        verification.ApprovedByAdminId = approvingAdminId;
        await _verifications.SaveChangesAsync(ct);

        var journeyEvent = new JourneyEvent
        {
            StudentId = verification.StudentId,
            EventType = next,
            Timestamp = DateTime.UtcNow,
            Source = EventSource.Manual
        };
        await _journey.AddJourneyEventAsync(journeyEvent, ct);
        await _journey.SaveChangesAsync(ct);

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

        verification.PickupApproved = false;
        verification.ApprovedByAdminId = actingAdminId;
        verification.Notes = request.Reason;
        await _verifications.SaveChangesAsync(ct);

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

    public async Task<List<GuardianVerificationHistoryDto>> GetHistoryAsync(int? studentId, CancellationToken ct = default) =>
        (await _verifications.GetHistoryAsync(studentId, ct))
            .Select(v => new GuardianVerificationHistoryDto(
                v.Id, v.StudentId, v.Student.FullName, v.GuardianId, v.Guardian.FullName,
                v.AttemptedAt, v.MatchScore, v.Result, v.PickupApproved))
            .ToList();
}
