using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

/// <summary>The RFID scan pipeline: card -> student -> state machine -> event + attendance.</summary>
public class RfidScanTests
{
    private sealed class FakeRfidRepository : IRfidRepository
    {
        public List<RfidCard> Cards { get; } = new();
        public List<RfidDevice> Devices { get; } = new();

        public Task<List<RfidCard>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(Cards.ToList());
        public Task<RfidCard?> GetByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Cards.FirstOrDefault(c => c.RfidCardId == id));
        public Task<RfidCard?> GetByCardUidAsync(string cardUid, CancellationToken ct = default) => Task.FromResult(Cards.FirstOrDefault(c => c.CardUid == cardUid));
        public Task AddAsync(RfidCard card, CancellationToken ct = default) { Cards.Add(card); return Task.CompletedTask; }
        public Task<List<RfidDevice>> GetDevicesAsync(CancellationToken ct = default) => Task.FromResult(Devices.ToList());
        public Task<RfidDevice?> GetDeviceByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Devices.FirstOrDefault(d => d.RfidDeviceId == id));
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeRfidRepository Rfid { get; } = new();
        public FakeStudentRepository Students { get; } = new();
        public FakeJourneyRepository Journey { get; } = new();
        public FakeNotificationRepository Notifications { get; } = new();
        public FakePushNotificationSender Push { get; } = new();
        public RfidService Service { get; }

        public Harness()
        {
            Service = new RfidService(Rfid, Students, Journey, Notifications, Push, new JourneyValidationService());
            Students.Students.Add(new Student { StudentId = 1, FullName = "Rahul Patel", DateOfBirth = new DateTime(2016, 4, 2), Status = RecordStatus.Active });
            Students.Links.Add(new StudentGuardian { StudentId = 1, GuardianId = 1, IsAuthorizedForPickup = true });
            Rfid.Cards.Add(new RfidCard { RfidCardId = 1, CardUid = "CARD-0001", StudentId = 1, Status = RfidCardStatus.Active });
            Rfid.Devices.Add(new RfidDevice { RfidDeviceId = 1, DeviceLabel = "Gate" });
        }

        public Task<RfidScanResultDto> Scan(JourneyEventType checkpoint) =>
            Service.ProcessScanAsync(new RfidScanRequest("CARD-0001", 1, checkpoint));
    }

    [Fact]
    public async Task Scan_ValidCheckpoint_RecordsEventUnderTheStudentLock()
    {
        var h = new Harness();

        var result = await h.Scan(JourneyEventType.SchoolArrival);

        Assert.True(result.EventRecorded);
        Assert.Single(h.Journey.Events);
        Assert.Single(h.Journey.AttendanceRecords);
        Assert.Equal(1, h.Journey.SerializedRuns);
    }

    [Fact]
    public async Task Scan_SameCheckpointTwice_SecondIsRejectedAndNotDuplicated()
    {
        var h = new Harness();
        await h.Scan(JourneyEventType.SchoolArrival);

        var second = await h.Scan(JourneyEventType.SchoolArrival);

        Assert.False(second.EventRecorded);
        Assert.Single(h.Journey.Events);
        Assert.Single(h.Journey.AttendanceRecords);
        Assert.Single(h.Notifications.Notifications);
    }

    [Fact]
    public async Task Scan_CycleLeftOpenYesterday_DoesNotBlockTodaysFirstCheckpoint()
    {
        var h = new Harness();
        // Arrived yesterday, but no departure was ever recorded.
        h.Journey.Events.Add(new JourneyEvent
        {
            StudentId = 1, EventType = JourneyEventType.SchoolArrival,
            Timestamp = DateTime.UtcNow.Date.AddHours(-12), Source = EventSource.Rfid
        });

        var result = await h.Scan(JourneyEventType.BusBoarding);

        Assert.True(result.EventRecorded);
        Assert.Equal(JourneyEventType.BusBoarding, result.RecordedEventType);
    }
}
