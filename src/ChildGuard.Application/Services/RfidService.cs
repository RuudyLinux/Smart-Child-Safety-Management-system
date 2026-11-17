using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class RfidService : IRfidService
{
    private readonly IRfidRepository _rfid;
    private readonly IStudentRepository _students;
    private readonly IJourneyRepository _journey;
    private readonly INotificationRepository _notifications;
    private readonly IPushNotificationSender _pushSender;
    private readonly JourneyValidationService _validation;
    private readonly SchoolClock _clock;
    private readonly RfidOptions _rfidOptions;
    private readonly IAuditTrail _audit;
    private readonly ISchoolScheduleProvider _schedule;

    public RfidService(
        IRfidRepository rfid,
        IStudentRepository students,
        IJourneyRepository journey,
        INotificationRepository notifications,
        IPushNotificationSender pushSender,
        JourneyValidationService validation,
        SchoolClock clock,
        RfidOptions rfidOptions,
        IAuditTrail? audit = null,
        ISchoolScheduleProvider? schedule = null)
    {
        _schedule = schedule ?? new ConfiguredSchoolScheduleProvider(new SchoolOptions());
        _audit = audit ?? NullAuditTrail.Instance;
        _clock = clock;
        _rfidOptions = rfidOptions;
        _rfid = rfid;
        _students = students;
        _journey = journey;
        _notifications = notifications;
        _pushSender = pushSender;
        _validation = validation;
    }

    public async Task<List<RfidCardListItemDto>> GetAllAsync(CancellationToken ct = default) =>
        (await _rfid.GetAllAsync(ct)).Select(ToListItem).ToList();

    public async Task<Paged<RfidCardListItemDto>> GetPageAsync(RfidCardListFilter filter, PageRequest page, CancellationToken ct = default)
    {
        var (cards, total) = await _rfid.GetPageAsync(filter, page, ct);
        return new Paged<RfidCardListItemDto>(cards.Select(ToListItem).ToList(), total);
    }

    public Task<RfidCardSummaryDto> GetSummaryAsync(CancellationToken ct = default) => _rfid.GetSummaryAsync(ct);

    private static RfidCardListItemDto ToListItem(RfidCard c) => new(
        c.RfidCardId, RfidMasking.Mask(c.CardUid), c.Status, c.StudentId, c.Student?.FullName, c.AssignedAt);

    public async Task<Result<RfidCardDetailDto>> GetByIdAsync(int cardId, CancellationToken ct = default)
    {
        var card = await _rfid.GetByIdAsync(cardId, ct);
        if (card is null) return Result<RfidCardDetailDto>.Failure("RFID card not found.");

        var history = await _rfid.GetHistoryAsync(cardId, ct);
        return Result<RfidCardDetailDto>.Success(new RfidCardDetailDto(
            card.RfidCardId, RfidMasking.Mask(card.CardUid), card.Status,
            card.StudentId, card.Student?.FullName, card.Student?.Grade, card.AssignedAt, card.CreatedAt,
            history, card.StudentId is null && !await _rfid.HasHistoryAsync(cardId, ct)));
    }

    public async Task<Result> AssignAsync(AssignRfidRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(request.StudentId, ct);
        if (student is null) return Result.Failure("Student not found.");
        // One card per student (also a unique key); refused here instead of failing on the save.
        if (student.RfidCard is not null) return Result.Failure(StudentHasCard(student.FullName));

        var existingByUid = await _rfid.GetByCardUidAsync(request.CardUid, ct);
        if (existingByUid is not null) return Result.Failure("This RFID card is already registered.");

        var card = new RfidCard
        {
            CardUid = request.CardUid,
            StudentId = request.StudentId,
            Status = RfidCardStatus.Active,
            AssignedAt = _clock.UtcNow
        };
        await _rfid.AddAsync(card, ct);
        await _rfid.AddAssignmentHistoryAsync(new RfidCardAssignment
        {
            RfidCard = card, StudentId = request.StudentId, AssignedAt = card.AssignedAt, RecordedAt = card.AssignedAt.Value
        }, ct);
        await _audit.SaveWithAuditAsync(_rfid.SaveChangesAsync,
            () => new AuditEvent("RfidCardAssigned", "RfidCard", card.RfidCardId,
                $"Card {RfidMasking.Mask(card.CardUid)} assigned to {student.FullName}")
            { StudentId = student.StudentId, Details = new { card = RfidMasking.Mask(card.CardUid), newCard = true } }, ct);
        return Result.Success();
    }

    public async Task<RfidCardCheckDto> CheckCardAsync(string cardUid, CancellationToken ct = default)
    {
        var uid = cardUid.Trim();
        var masked = RfidMasking.Mask(uid);
        var card = await _rfid.GetByCardUidAsync(uid, ct);
        if (card is null)
            return new RfidCardCheckDto(masked, RfidCardAvailability.New, true, null, null, null,
                "New card. It will be registered and assigned to this student.");
        if (card.StudentId is not null)
            return new RfidCardCheckDto(masked, RfidCardAvailability.AssignedToStudent, false, card.Status, card.StudentId, card.Student?.FullName,
                $"This card is already assigned to {card.Student?.FullName ?? "another student"}. Scan a different card.");
        if (card.Status != RfidCardStatus.Active)
            return new RfidCardCheckDto(masked, RfidCardAvailability.Blocked, false, card.Status, null, null,
                $"This card is marked {card.Status} and cannot be assigned. Scan a different card.");
        return new RfidCardCheckDto(masked, RfidCardAvailability.Available, true, card.Status, null, null,
            "Registered card, not assigned to anyone. It will be assigned to this student.");
    }

    // ------------------------------------------------------------------ inventory

    public const string CardNotFound = "RFID card not found.";
    public const string RetiredIsFinal = "A retired card stays retired. Register a new card instead.";
    public const string RetireNeedsUnassign = "Unassign or replace this card before retiring it.";
    public const string CardChangedConcurrently = "This card or student was changed at the same moment by someone else. Reload and try again.";
    public static string StudentHasCard(string name) => $"{name} already has an RFID card. Replace or unassign it first.";
    private static string NotAssignable(RfidCard c) => c.StudentId is not null
        ? $"Card {RfidMasking.Mask(c.CardUid)} is already assigned to {c.Student?.FullName ?? "another student"}."
        : $"Card {RfidMasking.Mask(c.CardUid)} is marked {c.Status} and cannot be assigned.";

    private static Result<RfidOperationResultDto> Fail(string error) => Result<RfidOperationResultDto>.Failure(error);
    private static Result<RfidOperationResultDto> Done(string message, int cardId, int? studentId) =>
        Result<RfidOperationResultDto>.Success(new RfidOperationResultDto(message, cardId, studentId));

    /// <summary>Loads a student who may receive a card: exists, active (not archived) and holds no card.</summary>
    private async Task<(Student? Student, string? Error)> StudentForCardAsync(int studentId, CancellationToken ct)
    {
        var student = await _students.GetByIdAsync(studentId, ct);
        if (student is null) return (null, "Student not found.");
        if (student.Status != RecordStatus.Active) return (null, $"{student.FullName} is not active, so a card cannot be assigned.");
        if (student.RfidCard is not null) return (null, StudentHasCard(student.FullName));
        return (student, null);
    }

    private async Task StartAssignmentAsync(RfidCard card, Student student, int adminId, DateTime now, CancellationToken ct)
    {
        card.StudentId = student.StudentId;
        card.Student = student;
        card.AssignedAt = now;
        await _rfid.AddAssignmentHistoryAsync(new RfidCardAssignment
        {
            RfidCardId = card.RfidCardId, StudentId = student.StudentId, AssignedAt = now, RecordedAt = now, AssignedByAdminId = adminId
        }, ct);
    }

    private async Task EndAssignmentAsync(RfidCard card, string reason, int adminId, DateTime now, CancellationToken ct)
    {
        var open = await _rfid.GetOpenAssignmentAsync(card.RfidCardId, ct);
        if (open is not null)
        {
            open.EndedAt = now;
            open.EndReason = reason.Length > 200 ? reason[..200] : reason;
            open.EndedByAdminId = adminId;
        }
        card.StudentId = null;
        card.Student = null;
        card.AssignedAt = null;
    }

    public async Task<Result<RfidOperationResultDto>> RegisterAsync(RegisterRfidCardRequest request, int adminId, CancellationToken ct = default)
    {
        var uid = request.CardUid.Trim();
        if (await _rfid.GetByCardUidAsync(uid, ct) is not null) return Fail("This RFID card is already registered.");
        var card = new RfidCard { CardUid = uid, Status = RfidCardStatus.Active, CreatedAt = _clock.UtcNow };
        await _rfid.AddAsync(card, ct);
        var saved = true;
        await _audit.SaveWithAuditAsync(async c => saved = await _rfid.TrySaveChangesAsync(c),
            () => new AuditEvent("RfidCardRegistered", "RfidCard", card.RfidCardId, $"Card {RfidMasking.Mask(uid)} registered as a spare")
            { Details = new { card = RfidMasking.Mask(uid) } }, ct);
        return saved ? Done($"Card {RfidMasking.Mask(uid)} registered and available.", card.RfidCardId, null)
                     : Fail("This RFID card is already registered.");
    }

    public async Task<Result<RfidOperationResultDto>> AssignRegisteredAsync(int cardId, AssignRegisteredCardRequest request, int adminId, CancellationToken ct = default)
    {
        string? error = null;
        Student? student = null;
        var result = await _rfid.RunLockedAsync(new[] { cardId }, async tx =>
        {
            var card = await _rfid.GetByIdAsync(cardId, tx);
            if (card is null) return Result.Failure(error = CardNotFound);
            if (card.StudentId is not null || card.Status != RfidCardStatus.Active) return Result.Failure(error = NotAssignable(card));
            (student, error) = await StudentForCardAsync(request.StudentId, tx);
            if (error is not null) return Result.Failure(error);

            await StartAssignmentAsync(card, student!, adminId, _clock.UtcNow, tx);
            _audit.Record(new AuditEvent("RfidCardAssigned", "RfidCard", cardId, $"Card {RfidMasking.Mask(card.CardUid)} assigned to {student!.FullName}")
            { StudentId = student.StudentId, Details = new { card = RfidMasking.Mask(card.CardUid), newCard = false } });
            return await _rfid.TrySaveChangesAsync(tx) ? Result.Success() : Result.Failure(error = CardChangedConcurrently);
        }, ct);
        return result.Succeeded ? Done($"Card assigned to {student!.FullName}.", cardId, student.StudentId) : Fail(error ?? result.Error!);
    }

    public async Task<Result<RfidOperationResultDto>> UnassignAsync(int cardId, UnassignRfidRequest request, int adminId, CancellationToken ct = default)
    {
        string? error = null;
        string? studentName = null;
        var result = await _rfid.RunLockedAsync(new[] { cardId }, async tx =>
        {
            var card = await _rfid.GetByIdAsync(cardId, tx);
            if (card is null) return Result.Failure(error = CardNotFound);
            if (card.StudentId is not int studentId) return Result.Failure(error = "This card is not assigned to anyone.");
            studentName = card.Student?.FullName;
            var from = card.Status;
            await EndAssignmentAsync(card, $"Unassigned: {request.Reason.Trim()}", adminId, _clock.UtcNow, tx);
            if (request.NewStatus is { } status) card.Status = status;
            _audit.Record(new AuditEvent("RfidCardUnassigned", "RfidCard", cardId, $"Card {RfidMasking.Mask(card.CardUid)} taken from {studentName}")
            {
                StudentId = studentId, Reason = request.Reason.Trim(),
                Details = new { card = RfidMasking.Mask(card.CardUid), statusFrom = from, statusTo = card.Status }
            });
            await _rfid.SaveChangesAsync(tx);
            return Result.Success();
        }, ct);
        return result.Succeeded ? Done($"Card unassigned from {studentName}.", cardId, null) : Fail(error ?? result.Error!);
    }

    public async Task<Result<RfidOperationResultDto>> ReplaceAsync(int cardId, ReplaceRfidRequest request, int adminId, CancellationToken ct = default)
    {
        var uid = request.NewCardUid.Trim();
        var existingNew = await _rfid.GetByCardUidAsync(uid, ct);
        if (existingNew?.RfidCardId == cardId) return Fail("The replacement must be a different card.");
        var lockIds = existingNew is null ? new[] { cardId } : new[] { cardId, existingNew.RfidCardId };

        string? error = null;
        string? studentName = null;
        int? studentId = null;
        int newCardId = 0;
        var result = await _rfid.RunLockedAsync(lockIds, async tx =>
        {
            var old = await _rfid.GetByIdAsync(cardId, tx);
            if (old is null) return Result.Failure(error = CardNotFound);
            if (old.StudentId is not int sid) return Result.Failure(error = "This card is not assigned to anyone, so there is nothing to replace. Assign it instead.");
            var student = old.Student!;
            studentId = sid;
            studentName = student.FullName;

            var now = _clock.UtcNow;
            var replacement = existingNew is null ? null : await _rfid.GetByIdAsync(existingNew.RfidCardId, tx);
            if (replacement is not null && (replacement.StudentId is not null || replacement.Status != RfidCardStatus.Active))
                return Result.Failure(error = NotAssignable(replacement));

            var oldFrom = old.Status;
            await EndAssignmentAsync(old, $"Replaced: {request.Reason.Trim()}", adminId, now, tx);
            old.Status = request.OldCardStatus;
            // The old link is cleared and saved first: one card per student is a unique key.
            await _rfid.SaveChangesAsync(tx);

            var isNew = replacement is null;
            if (replacement is null)
            {
                replacement = new RfidCard { CardUid = uid, Status = RfidCardStatus.Active, CreatedAt = now };
                await _rfid.AddAsync(replacement, tx);
                if (!await _rfid.TrySaveChangesAsync(tx)) return Result.Failure(error = "The new card was registered by someone else at the same moment. Try again.");
            }
            await StartAssignmentAsync(replacement, student, adminId, now, tx);
            newCardId = replacement.RfidCardId;
            _audit.Record(new AuditEvent("RfidCardReplaced", "RfidCard", cardId,
                $"Card {RfidMasking.Mask(old.CardUid)} of {student.FullName} replaced by {RfidMasking.Mask(replacement.CardUid)}")
            {
                StudentId = sid, Reason = request.Reason.Trim(),
                Details = new { oldCard = RfidMasking.Mask(old.CardUid), oldStatusFrom = oldFrom, oldStatusTo = old.Status, newCard = RfidMasking.Mask(replacement.CardUid), newCardRegistered = isNew, newCardId = replacement.RfidCardId }
            });
            return await _rfid.TrySaveChangesAsync(tx) ? Result.Success() : Result.Failure(error = CardChangedConcurrently);
        }, ct);
        return result.Succeeded ? Done($"Card replaced for {studentName}.", newCardId, studentId) : Fail(error ?? result.Error!);
    }

    public async Task<Result<RfidOperationResultDto>> ReassignAsync(int cardId, ReassignRfidRequest request, int adminId, CancellationToken ct = default)
    {
        string? error = null;
        string? toName = null;
        var result = await _rfid.RunLockedAsync(new[] { cardId }, async tx =>
        {
            var card = await _rfid.GetByIdAsync(cardId, tx);
            if (card is null) return Result.Failure(error = CardNotFound);
            if (card.StudentId is not int fromId) return Result.Failure(error = "This card is not assigned to anyone. Assign it instead.");
            if (fromId == request.StudentId) return Result.Failure(error = "The card already belongs to this student.");
            if (card.Status != RfidCardStatus.Active) return Result.Failure(error = $"This card is marked {card.Status}; only an active card can be reassigned.");
            var fromName = card.Student?.FullName;
            var (target, targetError) = await StudentForCardAsync(request.StudentId, tx);
            if (targetError is not null) return Result.Failure(error = targetError);
            toName = target!.FullName;

            var now = _clock.UtcNow;
            await EndAssignmentAsync(card, $"Reassigned: {request.Reason.Trim()}", adminId, now, tx);
            await _rfid.SaveChangesAsync(tx);
            await StartAssignmentAsync(card, target, adminId, now, tx);
            _audit.Record(new AuditEvent("RfidCardReassigned", "RfidCard", cardId, $"Card {RfidMasking.Mask(card.CardUid)} moved from {fromName} to {toName}")
            {
                StudentId = target.StudentId, Reason = request.Reason.Trim(),
                Details = new { card = RfidMasking.Mask(card.CardUid), fromStudentId = fromId, toStudentId = target.StudentId }
            });
            return await _rfid.TrySaveChangesAsync(tx) ? Result.Success() : Result.Failure(error = CardChangedConcurrently);
        }, ct);
        return result.Succeeded ? Done($"Card reassigned to {toName}.", cardId, request.StudentId) : Fail(error ?? result.Error!);
    }

    public async Task<Result<RfidOperationResultDto>> DeleteAsync(int cardId, string reason, int adminId, CancellationToken ct = default)
    {
        string? error = null;
        var result = await _rfid.RunLockedAsync(new[] { cardId }, async tx =>
        {
            var card = await _rfid.GetByIdAsync(cardId, tx);
            if (card is null) return Result.Failure(error = CardNotFound);
            if (card.StudentId is not null || await _rfid.HasHistoryAsync(cardId, tx))
                return Result.Failure(error = "This card has been used, so it is kept as history. Mark it Retired instead.");
            var masked = RfidMasking.Mask(card.CardUid);
            _rfid.Remove(card);
            _audit.Record(new AuditEvent("RfidCardDeleted", "RfidCard", cardId, $"Card {masked} permanently deleted")
            { Reason = reason.Trim(), Details = new { card = masked, card.Status } });
            await _rfid.SaveChangesAsync(tx);
            return Result.Success();
        }, ct);
        return result.Succeeded ? Done("Card permanently deleted.", cardId, null) : Fail(error ?? result.Error!);
    }

    public async Task<Result> UpdateStatusAsync(int cardId, UpdateRfidStatusRequest request, CancellationToken ct = default)
    {
        var card = await _rfid.GetByIdAsync(cardId, ct);
        if (card is null) return Result.Failure("RFID card not found.");
        if (card.Status == RfidCardStatus.Retired && request.Status != RfidCardStatus.Retired) return Result.Failure(RetiredIsFinal);
        if (request.Status == RfidCardStatus.Retired && card.StudentId is not null) return Result.Failure(RetireNeedsUnassign);

        if (card.Status != request.Status)
            _audit.Record(new AuditEvent("RfidCardStatusChanged", "RfidCard", cardId,
                $"Card {RfidMasking.Mask(card.CardUid)} marked {request.Status}")
            { StudentId = card.StudentId, Details = new { card = RfidMasking.Mask(card.CardUid), from = card.Status, to = request.Status } });
        card.Status = request.Status;
        await _rfid.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<List<RfidDeviceDto>> GetDevicesAsync(CancellationToken ct = default)
    {
        var devices = await _rfid.GetDevicesAsync(ct);
        return devices.Select(d => new RfidDeviceDto(d.RfidDeviceId, d.DeviceLabel, d.ConnectionStatus, d.LastSeenAt, d.ApiKeyHash is not null, d.ApiKeyIssuedAt)).ToList();
    }

    public async Task<Result<RfidDeviceDto>> CreateDeviceAsync(CreateRfidDeviceRequest request, CancellationToken ct = default)
    {
        var label = request.DeviceLabel.Trim();
        if (await LabelTakenAsync(label, null, ct))
            return Result<RfidDeviceDto>.Failure("A scanner with this label already exists.");

        // A new scanner starts disconnected and without a key; it is usable from the
        // pickup desk at once, and a key is issued from Settings for standalone readers.
        var device = new RfidDevice { DeviceLabel = label };
        await _rfid.AddDeviceAsync(device, ct);
        await _audit.SaveWithAuditAsync(_rfid.SaveChangesAsync,
            () => new AuditEvent("RfidDeviceCreated", "RfidDevice", device.RfidDeviceId, $"Scanner {label} added"), ct);
        return Result<RfidDeviceDto>.Success(new RfidDeviceDto(device.RfidDeviceId, device.DeviceLabel, device.ConnectionStatus, device.LastSeenAt));
    }

    public async Task<Result> RenameDeviceAsync(int deviceId, UpdateRfidDeviceRequest request, CancellationToken ct = default)
    {
        var device = await _rfid.GetDeviceByIdAsync(deviceId, ct);
        if (device is null) return Result.Failure(DeviceNotFound);

        var label = request.DeviceLabel.Trim();
        if (await LabelTakenAsync(label, deviceId, ct))
            return Result.Failure("A scanner with this label already exists.");

        if (device.DeviceLabel != label)
            _audit.Record(new AuditEvent("RfidDeviceRenamed", "RfidDevice", deviceId, $"Scanner {device.DeviceLabel} renamed to {label}")
            { Details = new { from = device.DeviceLabel, to = label } });
        device.DeviceLabel = label;
        await _rfid.SaveChangesAsync(ct);
        return Result.Success();
    }

    public const string DeviceNotFound = "Scanner device not found.";

    private async Task<bool> LabelTakenAsync(string label, int? exceptId, CancellationToken ct) =>
        (await _rfid.GetDevicesAsync(ct)).Any(d => d.RfidDeviceId != exceptId &&
            string.Equals(d.DeviceLabel, label, StringComparison.OrdinalIgnoreCase));

    public async Task<Result> HeartbeatAsync(int deviceId, CancellationToken ct = default)
    {
        var device = await _rfid.GetDeviceByIdAsync(deviceId, ct);
        if (device is null) return Result.Failure("Scanner device not recognized.");

        device.ConnectionStatus = RfidConnectionStatus.Connected;
        device.LastSeenAt = _clock.UtcNow;
        await _rfid.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<DeviceCredentialDto>> IssueDeviceKeyAsync(int deviceId, CancellationToken ct = default)
    {
        var device = await _rfid.GetDeviceByIdAsync(deviceId, ct);
        if (device is null) return Result<DeviceCredentialDto>.Failure("Scanner device not recognized.");

        // Issuing again rotates: the previous key stops working on save.
        var key = DeviceKeys.Generate();
        var rotated = device.ApiKeyHash is not null;
        device.ApiKeyHash = DeviceKeys.Hash(key);
        device.ApiKeyIssuedAt = _clock.UtcNow;
        // The action only: neither the key nor its hash is recorded.
        _audit.Record(new AuditEvent(rotated ? "RfidDeviceKeyRotated" : "RfidDeviceKeyIssued", "RfidDevice", deviceId,
            $"Scanner {device.DeviceLabel}: credential {(rotated ? "rotated" : "issued")}"));
        await _rfid.SaveChangesAsync(ct);
        return Result<DeviceCredentialDto>.Success(
            new DeviceCredentialDto(device.RfidDeviceId, device.DeviceLabel, key, device.ApiKeyIssuedAt.Value));
    }

    public async Task<Result> RevokeDeviceKeyAsync(int deviceId, CancellationToken ct = default)
    {
        var device = await _rfid.GetDeviceByIdAsync(deviceId, ct);
        if (device is null) return Result.Failure("Scanner device not recognized.");
        if (device.ApiKeyHash is not null)
            _audit.Record(new AuditEvent("RfidDeviceKeyRevoked", "RfidDevice", deviceId, $"Scanner {device.DeviceLabel}: credential revoked"));
        device.ApiKeyHash = null;
        device.ApiKeyIssuedAt = null;
        await _rfid.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<RfidIdentifyResultDto> IdentifyAsync(RfidIdentifyRequest request, CancellationToken ct = default)
    {
        var device = await _rfid.GetDeviceByIdAsync(request.RfidDeviceId, ct);
        if (device is null)
            return new RfidIdentifyResultDto(false, null, null, null, "Scanner device not recognized.");

        // Same card and student checks as a scan, without touching journey state.
        var card = await _rfid.GetByCardUidAsync(request.CardUid, ct);
        if (card is null || card.Status != RfidCardStatus.Active || card.StudentId is null)
            return new RfidIdentifyResultDto(false, null, null, RfidMasking.Mask(request.CardUid),
                "Card not recognized or not assigned to an active student.");

        var student = await _students.GetByIdAsync(card.StudentId.Value, ct);
        if (student is null || student.Status != RecordStatus.Active)
            return new RfidIdentifyResultDto(false, null, student?.FullName, RfidMasking.Mask(card.CardUid), "Student is not active.");

        return new RfidIdentifyResultDto(true, student.StudentId, student.FullName, RfidMasking.Mask(card.CardUid), "Student identified.");
    }

    public async Task<NextCheckpointDto> ResolveCheckpointAsync(RfidIdentifyRequest request, CancellationToken ct = default)
    {
        var found = await IdentifyAsync(request, ct);
        if (!found.StudentIdentified || found.StudentId is null)
            return new NextCheckpointDto(false, found.StudentId, found.StudentName, found.MaskedCardUid, null, null, found.Message, false);

        var studentId = found.StudentId.Value;
        var today = _clock.Today;
        var last = await _journey.GetLastEventSinceAsync(studentId, _clock.TodayStartUtc, ct);
        var travelsByBus = (await _students.GetTransportHistoryAsync(studentId, ct)).Any(a => a.Status == RecordStatus.Active);
        var info = (await _schedule.GetAsync(today, today, ct)).For(today);

        var decision = CheckpointResolver.Resolve(last?.EventType, travelsByBus, info, TimeOnly.FromDateTime(_clock.LocalNow));
        return new NextCheckpointDto(true, studentId, found.StudentName, found.MaskedCardUid,
            decision.Checkpoint, decision.Checkpoint is { } c ? JourneyEventNames.Label(c) : null, decision.Reason, decision.OffSchedule);
    }

    public async Task<RfidScanResultDto> ProcessScanAsync(RfidScanRequest request, CancellationToken ct = default)
    {
        var device = await _rfid.GetDeviceByIdAsync(request.RfidDeviceId, ct);
        if (device is null)
            return new RfidScanResultDto(false, null, null, null, false, null, "Scanner device not recognized.");

        // A scan reaching this point proves the physical reader is active — mark it
        // connected instead of leaving connection status permanently stale.
        device.ConnectionStatus = RfidConnectionStatus.Connected;
        device.LastSeenAt = _clock.UtcNow;
        await _rfid.SaveChangesAsync(ct);

        var card = await _rfid.GetByCardUidAsync(request.CardUid, ct);
        if (card is null || card.Status != RfidCardStatus.Active || card.StudentId is null)
            return new RfidScanResultDto(false, null, null, RfidMasking.Mask(request.CardUid), false, null,
                "Card not recognized or not assigned to an active student.");

        var student = await _students.GetByIdAsync(card.StudentId.Value, ct);
        if (student is null || student.Status != RecordStatus.Active)
            return new RfidScanResultDto(true, card.StudentId, student?.FullName, RfidMasking.Mask(card.CardUid),
                false, null, "Student is not active.");

        var masked = RfidMasking.Mask(card.CardUid);
        var scanId = string.IsNullOrWhiteSpace(request.ScanId) ? null : request.ScanId.Trim();

        // Read-validate-write under a per-student lock: two taps arriving together
        // (double tap, scanner retry, two readers) are applied one after the other,
        // so the second sees the first's event instead of both passing validation
        // against the same stale state.
        var outcome = await _journey.RunSerializedForStudentAsync(student.StudentId, async lockCt =>
        {
            // Retried request: the scanner resent a tap whose response it never got.
            if (scanId is not null)
            {
                var original = await _journey.GetByScanIdAsync(device.RfidDeviceId, scanId, lockCt);
                if (original is not null)
                    return ScanOutcome.Already(original);
            }

            var now = _clock.UtcNow;
            var lastEvent = await _journey.GetLastEventSinceAsync(student.StudentId, _clock.TodayStartUtc, lockCt);

            // Same checkpoint again within the window: a double tap or a retry without
            // a ScanId. Report success, write nothing, notify no one. Outside the window
            // the state machine below rejects it as a duplicate as before.
            if (lastEvent is not null
                && lastEvent.EventType == request.Checkpoint
                && now - lastEvent.Timestamp <= _rfidOptions.DuplicateScanWindow)
                return ScanOutcome.Already(lastEvent);

            var validation = _validation.ValidateNextEvent(lastEvent?.EventType, request.Checkpoint);
            if (!validation.Succeeded)
                return ScanOutcome.Rejected(validation.Error!);

            var created = new JourneyEvent
            {
                StudentId = student.StudentId,
                RfidCardId = card.RfidCardId,
                RfidDeviceId = device.RfidDeviceId,
                EventType = request.Checkpoint,
                Timestamp = now,
                Source = EventSource.Rfid,
                ScanId = scanId
            };
            await JourneyTransportSnapshot.StampAsync(_journey, created, lockCt);
            await _journey.AddJourneyEventAsync(created, lockCt);

            if (request.Checkpoint is JourneyEventType.SchoolArrival or JourneyEventType.SchoolDeparture)
            {
                var (date, time) = _clock.ToSchoolDateTime(now);
                await _journey.AddAttendanceAsync(new Attendance
                {
                    StudentId = student.StudentId,
                    RfidCardId = card.RfidCardId,
                    EventType = request.Checkpoint == JourneyEventType.SchoolArrival
                        ? AttendanceEventType.SchoolArrival
                        : AttendanceEventType.SchoolDeparture,
                    RecordedAtUtc = now,
                    Date = date,
                    Time = time
                }, lockCt);
            }

            await _journey.SaveChangesAsync(lockCt);
            return ScanOutcome.Recorded(created);
        }, ct);

        if (outcome.Event is null)
            return new RfidScanResultDto(true, student.StudentId, student.FullName, masked, false, null, outcome.Rejection!);

        if (outcome.AlreadyRecorded)
            return new RfidScanResultDto(true, student.StudentId, student.FullName, masked,
                true, outcome.Event.EventType, "Already recorded.", AlreadyRecorded: true);

        var journeyEvent = outcome.Event;
        var localTime = _clock.ToLocal(journeyEvent.Timestamp);

        // Notify every guardian linked to this student — in-app record now, push delivery in Notifications phase.
        var guardianLinks = await _students.GetLinkedGuardiansAsync(student.StudentId, ct);
        var allGuardianIds = guardianLinks.Select(g => g.GuardianId).Distinct().ToList();
        var label = JourneyEventNames.Label(request.Checkpoint);
        var title = $"{label} recorded";
        var message = $"{student.FullName}: {label} recorded at {localTime:t}.";
        foreach (var guardianId in allGuardianIds)
        {
            await _notifications.AddAsync(new Notification
            {
                GuardianId = guardianId,
                StudentId = student.StudentId,
                Type = NotificationType.Journey,
                Title = title,
                Message = message,
                RelatedEntityType = nameof(JourneyEvent),
                RelatedEntityId = journeyEvent.JourneyEventId
            }, ct);
        }
        await _notifications.SaveChangesAsync(ct);

        foreach (var guardianId in allGuardianIds)
        {
            await _pushSender.SendAsync(title, message, guardianId, ct);
        }

        return new RfidScanResultDto(true, student.StudentId, student.FullName, masked,
            true, request.Checkpoint, $"{label} recorded.");
    }

    private sealed record ScanOutcome(JourneyEvent? Event, bool AlreadyRecorded, string? Rejection)
    {
        public static ScanOutcome Recorded(JourneyEvent e) => new(e, false, null);
        public static ScanOutcome Already(JourneyEvent e) => new(e, true, null);
        public static ScanOutcome Rejected(string reason) => new(null, false, reason);
    }
}
