using ChildGuard.Application.Services;
using ChildGuard.Domain.Enums;
using Xunit;

namespace ChildGuard.Application.Tests;

/// <summary>
/// Regression coverage for the RFID/journey state machine — the piece the whole
/// "single scanner, no duplicate events, valid-next-event-only" requirement rests on.
/// Every case here was also exercised live against the real API/MySQL during
/// development; these tests pin that behavior so a future change can't silently break it.
/// </summary>
public class JourneyValidationServiceTests
{
    private readonly JourneyValidationService _sut = new();

    [Theory]
    [InlineData(null, JourneyEventType.BusBoarding)]
    [InlineData(null, JourneyEventType.SchoolArrival)] // guardian personally drops student off — no prior bus event
    [InlineData(JourneyEventType.BusBoarding, JourneyEventType.SchoolArrival)]
    [InlineData(JourneyEventType.SchoolArrival, JourneyEventType.SchoolDeparture)]
    [InlineData(JourneyEventType.SchoolDeparture, JourneyEventType.BusDropoff)]
    [InlineData(JourneyEventType.BusDropoff, JourneyEventType.BusBoarding)] // next day's cycle restarts
    [InlineData(JourneyEventType.BusDropoff, JourneyEventType.SchoolArrival)] // or restarts via guardian drop-off
    public void ValidateNextEvent_AllowsCorrectTransitions(JourneyEventType? lastEvent, JourneyEventType requested)
    {
        var result = _sut.ValidateNextEvent(lastEvent, requested);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(requested, result.Value);
    }

    [Theory]
    [InlineData(JourneyEventType.BusBoarding, JourneyEventType.BusBoarding)] // duplicate scan
    [InlineData(JourneyEventType.SchoolArrival, JourneyEventType.SchoolArrival)] // duplicate scan
    [InlineData(JourneyEventType.BusBoarding, JourneyEventType.SchoolDeparture)] // skips School Arrival
    [InlineData(JourneyEventType.BusBoarding, JourneyEventType.BusDropoff)] // skips two steps
    [InlineData(JourneyEventType.SchoolArrival, JourneyEventType.BusBoarding)] // goes backward
    [InlineData(JourneyEventType.SchoolArrival, JourneyEventType.BusDropoff)] // skips School Departure
    [InlineData(JourneyEventType.SchoolDeparture, JourneyEventType.SchoolArrival)] // goes backward
    [InlineData(JourneyEventType.SchoolDeparture, JourneyEventType.SchoolDeparture)] // duplicate scan
    [InlineData(JourneyEventType.BusDropoff, JourneyEventType.SchoolDeparture)] // invalid after cycle end
    [InlineData(JourneyEventType.BusDropoff, JourneyEventType.BusDropoff)] // duplicate scan
    public void ValidateNextEvent_RejectsInvalidTransitions(JourneyEventType? lastEvent, JourneyEventType requested)
    {
        var result = _sut.ValidateNextEvent(lastEvent, requested);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ValidateNextEvent_DuplicateScan_ErrorMessageNamesItAsDuplicate()
    {
        var result = _sut.ValidateNextEvent(JourneyEventType.SchoolArrival, JourneyEventType.SchoolArrival);

        Assert.False(result.Succeeded);
        Assert.Contains("duplicate", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
