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

    public RfidService(
        IRfidRepository rfid,
        IStudentRepository students,
        IJourneyRepository journey,
        INotificationRepository notifications,
        IPushNotificationSender pushSender,
        JourneyValidationService validation)
    {
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
            AssignedAt = DateTime.UtcNow
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
        return devices.Select(d => new RfidDeviceDto(d.RfidDeviceId, d.DeviceLabel, d.ConnectionStatus, d.LastSeenAt)).ToList();
    }

    public async Task<Result> HeartbeatAsync(int deviceId, CancellationToken ct = default)
    {
        var device = await _rfid.GetDeviceByIdAsync(deviceId, ct);
        if (device is null) return Result.Failure("Scanner device not recognized.");

        device.ConnectionStatus = RfidConnectionStatus.Connected;
        device.LastSeenAt = DateTime.UtcNow;
        await _rfid.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<RfidScanResultDto> ProcessScanAsync(RfidScanRequest request, CancellationToken ct = default)
    {
        var device = await _rfid.GetDeviceByIdAsync(request.RfidDeviceId, ct);
        if (device is null)
            return new RfidScanResultDto(false, null, null, null, false, null, "Scanner device not recognized.");

        // A scan reaching this point proves the physical reader is active — mark it
        // connected instead of leaving connection status permanently stale.
        device.ConnectionStatus = RfidConnectionStatus.Connected;
        device.LastSeenAt = DateTime.UtcNow;
        await _rfid.SaveChangesAsync(ct);

        var card = await _rfid.GetByCardUidAsync(request.CardUid, ct);
        if (card is null || card.Status != RfidCardStatus.Active || card.StudentId is null)
            return new RfidScanResultDto(false, null, null, RfidMasking.Mask(request.CardUid), false, null,
                "Card not recognized or not assigned to an active student.");

        var student = await _students.GetByIdAsync(card.StudentId.Value, ct);
        if (student is null || student.Status != RecordStatus.Active)
            return new RfidScanResultDto(true, card.StudentId, student?.FullName, RfidMasking.Mask(card.CardUid),
                false, null, "Student is not active.");

        var lastEvent = await _journey.GetLastEventAsync(student.StudentId, ct);
        var validation = _validation.ValidateNextEvent(lastEvent?.EventType, request.Checkpoint);

        if (!validation.Succeeded)
            return new RfidScanResultDto(true, student.StudentId, student.FullName, RfidMasking.Mask(card.CardUid),
                false, null, validation.Error!);

        var now = DateTime.UtcNow;
        var journeyEvent = new JourneyEvent
        {
            StudentId = student.StudentId,
            RfidCardId = card.RfidCardId,
            RfidDeviceId = device.RfidDeviceId,
            EventType = request.Checkpoint,
            Timestamp = now,
            Source = EventSource.Rfid
        };
        await _journey.AddJourneyEventAsync(journeyEvent, ct);

        if (request.Checkpoint is JourneyEventType.SchoolArrival or JourneyEventType.SchoolDeparture)
        {
            await _journey.AddAttendanceAsync(new Attendance
            {
                StudentId = student.StudentId,
                RfidCardId = card.RfidCardId,
                EventType = request.Checkpoint == JourneyEventType.SchoolArrival
                    ? AttendanceEventType.SchoolArrival
                    : AttendanceEventType.SchoolDeparture,
                Date = DateOnly.FromDateTime(now),
                Time = TimeOnly.FromDateTime(now)
            }, ct);
        }

        await _journey.SaveChangesAsync(ct);

        // Notify every guardian linked to this student — in-app record now, push delivery in Notifications phase.
        var guardianLinks = await _students.GetLinkedGuardiansAsync(student.StudentId, ct);
        var allGuardianIds = guardianLinks.Select(g => g.GuardianId).Distinct().ToList();
        var title = $"{request.Checkpoint} recorded";
        var message = $"{student.FullName}: {request.Checkpoint} recorded at {now:t}.";
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

        return new RfidScanResultDto(true, student.StudentId, student.FullName, RfidMasking.Mask(card.CardUid),
            true, request.Checkpoint, $"{request.Checkpoint} recorded successfully.");
    }
}
