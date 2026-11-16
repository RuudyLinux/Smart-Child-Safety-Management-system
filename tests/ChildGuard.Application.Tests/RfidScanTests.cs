using ChildGuard.Application.Common;
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
        public List<RfidCardAssignment> History { get; } = new();
        public Task AddAssignmentHistoryAsync(RfidCardAssignment entry, CancellationToken ct = default) { History.Add(entry); return Task.CompletedTask; }
        public Task<Application.Common.Result> RunLockedAsync(IReadOnlyCollection<int> cardIds, Func<CancellationToken, Task<Application.Common.Result>> work, CancellationToken ct = default) => work(ct);
        public Task LockCardsAsync(IReadOnlyCollection<int> cardIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task<RfidCardAssignment?> GetOpenAssignmentAsync(int cardId, CancellationToken ct = default) =>
            Task.FromResult(History.LastOrDefault(h => h.RfidCardId == cardId && h.EndedAt == null));
        public Task<List<Application.DTOs.RfidCardAssignmentDto>> GetHistoryAsync(int cardId, CancellationToken ct = default) => Task.FromResult(new List<Application.DTOs.RfidCardAssignmentDto>());
        public Task<bool> HasHistoryAsync(int cardId, CancellationToken ct = default) => Task.FromResult(History.Any(h => h.RfidCardId == cardId));
        public void Remove(RfidCard card) => Cards.Remove(card);
        public Task<bool> TrySaveChangesAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<List<RfidDevice>> GetDevicesAsync(CancellationToken ct = default) => Task.FromResult(Devices.ToList());
        public Task<RfidDevice?> GetDeviceByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Devices.FirstOrDefault(d => d.RfidDeviceId == id));
        public Task AddDeviceAsync(RfidDevice device, CancellationToken ct = default) { Devices.Add(device); return Task.CompletedTask; }
        public Task<RfidDevice?> GetDeviceByKeyHashAsync(string keyHash, CancellationToken ct = default) => Task.FromResult(Devices.FirstOrDefault(d => d.ApiKeyHash == keyHash));
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeRfidRepository Rfid { get; } = new();
        public FakeStudentRepository Students { get; } = new();
        public FakeJourneyRepository Journey { get; } = new();
        public FakeNotificationRepository Notifications { get; } = new();
        public FakePushNotificationSender Push { get; } = new();
        public SchoolClock Clock { get; }
        public MutableTimeProvider Time { get; }
        public RfidService Service { get; }

        public Harness(DateTime? utcNow = null)
        {
            (Clock, Time) = TestClock.Create(utcNow);
            Service = new RfidService(Rfid, Students, Journey, Notifications, Push, new JourneyValidationService(), Clock, new RfidOptions());
            Students.Students.Add(new Student { StudentId = 1, FullName = "Rahul Patel", DateOfBirth = new DateTime(2016, 4, 2), Status = RecordStatus.Active });
            Students.Links.Add(new StudentGuardian { StudentId = 1, GuardianId = 1, IsAuthorizedForPickup = true });
            Rfid.Cards.Add(new RfidCard { RfidCardId = 1, CardUid = "CARD-0001", StudentId = 1, Status = RfidCardStatus.Active });
            Rfid.Devices.Add(new RfidDevice { RfidDeviceId = 1, DeviceLabel = "Gate" });
        }

        public Task<RfidScanResultDto> Scan(JourneyEventType checkpoint, string? scanId = null) =>
            Service.ProcessScanAsync(new RfidScanRequest("CARD-0001", 1, checkpoint, scanId));
    }

    [Fact]
    public async Task Scan_ValidCheckpoint_RecordsEventUnderTheStudentLock()
    {
        var h = new Harness();

        var result = await h.Scan(JourneyEventType.SchoolArrival);

        Assert.True(result.EventRecorded);
        Assert.False(result.AlreadyRecorded);
        Assert.Single(h.Journey.Events);
        Assert.Single(h.Journey.AttendanceRecords);
        Assert.Equal(1, h.Journey.SerializedRuns);
    }

    [Fact]
    public async Task Scan_SameCheckpointWithinWindow_ReportsAlreadyRecordedAndWritesNothing()
    {
        var h = new Harness();
        await h.Scan(JourneyEventType.SchoolArrival);
        h.Time.Advance(TimeSpan.FromSeconds(119));

        var second = await h.Scan(JourneyEventType.SchoolArrival);

        Assert.True(second.EventRecorded);
        Assert.True(second.AlreadyRecorded);
        Assert.Equal("Already recorded.", second.Message);
        Assert.Single(h.Journey.Events);
        Assert.Single(h.Journey.AttendanceRecords);
        Assert.Single(h.Notifications.Notifications);
        Assert.Single(h.Push.Sent);
    }

    [Fact]
    public async Task Scan_SameCheckpointAfterWindow_IsRejectedAsDuplicateAndWritesNothing()
    {
        var h = new Harness();
        await h.Scan(JourneyEventType.SchoolArrival);
        h.Time.Advance(TimeSpan.FromSeconds(121));

        var second = await h.Scan(JourneyEventType.SchoolArrival);

        Assert.False(second.EventRecorded);
        Assert.False(second.AlreadyRecorded);
        Assert.Contains("duplicate", second.Message);
        Assert.Single(h.Journey.Events);
    }

    [Fact]
    public async Task Scan_RetriedScanId_ReturnsOriginalEvenAfterWindowAndLaterEvents()
    {
        var h = new Harness();
        await h.Scan(JourneyEventType.BusBoarding, scanId: "gate-1:0001");
        h.Time.Advance(TimeSpan.FromMinutes(30));
        await h.Scan(JourneyEventType.SchoolArrival, scanId: "gate-1:0002");
        h.Time.Advance(TimeSpan.FromMinutes(10));

        // A late retry of the first tap must not be treated as a new boarding.
        var retry = await h.Scan(JourneyEventType.BusBoarding, scanId: "gate-1:0001");

        Assert.True(retry.AlreadyRecorded);
        Assert.Equal(JourneyEventType.BusBoarding, retry.RecordedEventType);
        Assert.Equal(2, h.Journey.Events.Count);
        Assert.Equal(2, h.Notifications.Notifications.Count);
    }

    [Fact]
    public async Task Scan_FullCycleThenSameCheckpointAgain_IsAcceptedAsNewCycle()
    {
        var h = new Harness(new DateTime(2026, 11, 6, 1, 30, 0, DateTimeKind.Utc)); // 07:00 IST
        foreach (var checkpoint in new[] { JourneyEventType.BusBoarding, JourneyEventType.SchoolArrival, JourneyEventType.SchoolDeparture, JourneyEventType.BusDropoff })
        {
            Assert.True((await h.Scan(checkpoint)).EventRecorded);
            h.Time.Advance(TimeSpan.FromMinutes(30));
        }

        var again = await h.Scan(JourneyEventType.BusBoarding);

        Assert.True(again.EventRecorded);
        Assert.False(again.AlreadyRecorded);
        Assert.Equal(5, h.Journey.Events.Count);
    }

    [Fact]
    public async Task Scan_CycleLeftOpenYesterday_DoesNotBlockTodaysFirstCheckpoint()
    {
        var h = new Harness();
        // Arrived yesterday (school time), but no departure was ever recorded.
        h.Journey.Events.Add(new JourneyEvent
        {
            StudentId = 1, EventType = JourneyEventType.SchoolArrival,
            Timestamp = h.Clock.TodayStartUtc.AddHours(-12), Source = EventSource.Rfid
        });

        var result = await h.Scan(JourneyEventType.BusBoarding);

        Assert.True(result.EventRecorded);
        Assert.Equal(JourneyEventType.BusBoarding, result.RecordedEventType);
    }

    [Fact]
    public async Task Scan_AttendanceIsStampedWithSchoolLocalDateAndTime()
    {
        // 03:15 UTC = 08:45 at the school (UTC+05:30).
        var h = new Harness(new DateTime(2026, 11, 6, 3, 15, 0, DateTimeKind.Utc));

        await h.Scan(JourneyEventType.SchoolArrival);

        var row = Assert.Single(h.Journey.AttendanceRecords);
        Assert.Equal(new DateOnly(2026, 11, 6), row.Date);
        Assert.Equal(new TimeOnly(8, 45), row.Time);
        Assert.Equal(new DateTime(2026, 11, 6, 3, 15, 0), row.RecordedAtUtc);
    }

    [Fact]
    public async Task Scan_JustAfterLocalMidnight_BelongsToTheNewSchoolDay()
    {
        // 18:20 UTC on 5 Nov = 23:50 on 5 Nov at the school.
        var h = new Harness(new DateTime(2026, 11, 5, 18, 20, 0, DateTimeKind.Utc));
        await h.Scan(JourneyEventType.SchoolArrival);

        // 18:40 UTC = 00:10 on 6 Nov at the school (still 5 Nov in UTC): a new school day,
        // so the open cycle from 23:50 does not block, and the date rolls over.
        h.Time.UtcNow = new DateTime(2026, 11, 5, 18, 40, 0, DateTimeKind.Utc);
        var next = await h.Scan(JourneyEventType.SchoolArrival);

        Assert.True(next.EventRecorded);
        Assert.False(next.AlreadyRecorded);
        Assert.Equal(new[] { new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 6) },
            h.Journey.AttendanceRecords.Select(a => a.Date).ToArray());
    }

    [Fact]
    public async Task Scan_NotificationShowsSchoolLocalTime()
    {
        var h = new Harness(new DateTime(2026, 11, 6, 3, 15, 0, DateTimeKind.Utc)); // 08:45 IST

        await h.Scan(JourneyEventType.SchoolArrival);

        var note = Assert.Single(h.Notifications.Notifications);
        Assert.Contains(new DateTime(2026, 11, 6, 8, 45, 0).ToString("t"), note.Message);
    }

    // Guardians read these on their phone: plain words, never enum identifiers.
    [Theory]
    [InlineData(JourneyEventType.BusBoarding, "Bus boarding")]
    [InlineData(JourneyEventType.SchoolArrival, "School arrival")]
    public async Task Scan_NotificationAndMessage_UsePlainCheckpointNames(JourneyEventType checkpoint, string label)
    {
        var h = new Harness();

        var result = await h.Scan(checkpoint);

        var note = Assert.Single(h.Notifications.Notifications);
        Assert.Equal($"{label} recorded", note.Title);
        Assert.StartsWith($"Rahul Patel: {label} recorded at ", note.Message);
        Assert.Equal($"{label} recorded.", result.Message);
        Assert.DoesNotContain(checkpoint.ToString(), note.Message);
    }
}
