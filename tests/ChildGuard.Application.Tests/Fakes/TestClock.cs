using ChildGuard.Application.Common;

namespace ChildGuard.Application.Tests.Fakes;

/// <summary>A TimeProvider whose "now" the test sets and advances.</summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    public MutableTimeProvider(DateTime utcNow) => UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public DateTime UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

internal static class TestClock
{
    public const string SchoolZone = "Asia/Kolkata"; // UTC+05:30, no DST

    /// <summary>10:00 at the school on Fri 6 Nov 2026 (04:30 UTC) — a normal school morning.</summary>
    public static readonly DateTime DefaultUtc = new(2026, 11, 6, 4, 30, 0, DateTimeKind.Utc);

    public static (SchoolClock Clock, MutableTimeProvider Time) Create(DateTime? utcNow = null, string zone = SchoolZone)
    {
        var time = new MutableTimeProvider(utcNow ?? DefaultUtc);
        return (new SchoolClock(new SchoolOptions { TimeZone = zone }, time), time);
    }
}
