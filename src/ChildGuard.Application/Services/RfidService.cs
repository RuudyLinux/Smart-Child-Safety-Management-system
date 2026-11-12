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

    public RfidService(
        IRfidRepository rfid,
        IStudentRepository students,
        IJourneyRepository journey,
        INotificationRepository notifications,
        IPushNotificationSender pushSender,
        JourneyValidationService validation,
        SchoolClock clock,
        RfidOptions rfidOptions)
    {
        _clock = clock;
        _rfidOptions = rfidOptions;
        _rfid = rfid;
        _students = students;
        _journey = journey;
        _notifications = notifications;
        _pushSender = pushSender;
        _validation = validation;
    }

    public async Task<List<RfidCardListItemDto>> GetAllAsync(CancellationToken ct = default)
    {
        var cards = await _rfid.GetAllAsync(ct);
        return cards.Select(c => new RfidCardListItemDto(
            c.RfidCardId, RfidMasking.Mask(c.CardUid), c.Status,
            c.StudentId, c.Student?.FullName, c.AssignedAt)).ToList();
    }

    public async Task<Result<RfidCardDetailDto>> GetByIdAsync(int cardId, CancellationToken ct = default)
    {
        var card = await _rfid.GetByIdAsync(cardId, ct);
        if (card is null) return Result<RfidCardDetailDto>.Failure("RFID card not found.");

        return Result<RfidCardDetailDto>.Success(new RfidCardDetailDto(
            card.RfidCardId, RfidMasking.Mask(card.CardUid), card.Status,
            card.StudentId, card.Student?.FullName, card.Student?.Grade, card.AssignedAt, card.CreatedAt));
    }

    public async Task<Result> AssignAsync(AssignRfidRequest request, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(request.StudentId, ct);
        if (student is null) return Result.Failure("Student not found.");

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
        await _rfid.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateStatusAsync(int cardId, UpdateRfidStatusRequest request, CancellationToken ct = default)
    {
        var card = await _rfid.GetByIdAsync(cardId, ct);
        if (card is null) return Result.Failure("RFID card not found.");

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
        await _rfid.SaveChangesAsync(ct);
        return Result<RfidDeviceDto>.Success(new RfidDeviceDto(device.RfidDeviceId, device.DeviceLabel, device.ConnectionStatus, device.LastSeenAt));
    }

    public async Task<Result> RenameDeviceAsync(int deviceId, UpdateRfidDeviceRequest request, CancellationToken ct = default)
    {
        var device = await _rfid.GetDeviceByIdAsync(deviceId, ct);
        if (device is null) return Result.Failure(DeviceNotFound);

        var label = request.DeviceLabel.Trim();
        if (await LabelTakenAsync(label, deviceId, ct))
            return Result.Failure("A scanner with this label already exists.");

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
        device.ApiKeyHash = DeviceKeys.Hash(key);
        device.ApiKeyIssuedAt = _clock.UtcNow;
        await _rfid.SaveChangesAsync(ct);
        return Result<DeviceCredentialDto>.Success(
            new DeviceCredentialDto(device.RfidDeviceId, device.DeviceLabel, key, device.ApiKeyIssuedAt.Value));
    }

    public async Task<Result> RevokeDeviceKeyAsync(int deviceId, CancellationToken ct = default)
    {
        var device = await _rfid.GetDeviceByIdAsync(deviceId, ct);
        if (device is null) return Result.Failure("Scanner device not recognized.");
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
