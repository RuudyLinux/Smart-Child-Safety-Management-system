using ChildGuard.Application.DTOs;
using ChildGuard.Application.Validators;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

public class LifecycleValidationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("too short", false)]
    [InlineData("          padded    ", false)]
    [InlineData("Left the school in March.", true)]
    public void ArchiveAndDelete_NeedAMeaningfulReason(string? reason, bool valid)
    {
        Assert.Equal(valid, new ArchiveRecordRequestValidator().Validate(new ArchiveRecordRequest(reason!)).IsValid);
        Assert.Equal(valid, new DeleteRecordRequestValidator().Validate(new DeleteRecordRequest(reason!)).IsValid);
    }

    [Fact]
    public void Reasons_AreLimitedTo500Characters()
    {
        var tooLong = new string('x', 501);
        Assert.False(new ArchiveRecordRequestValidator().Validate(new ArchiveRecordRequest(tooLong)).IsValid);
        Assert.False(new RestoreRecordRequestValidator().Validate(new RestoreRecordRequest(tooLong)).IsValid);
        Assert.True(new RestoreRecordRequestValidator().Validate(new RestoreRecordRequest(null)).IsValid);
    }

    [Fact]
    public void FullRecordEdit_IsValidated()
    {
        var v = new UpdateStudentRecordRequestValidator();
        Assert.True(v.Validate(new UpdateStudentRecordRequest("Asha", new DateTime(2016, 1, 1), "4", "B", "ADM-1", Gender.Female)).IsValid);
        Assert.False(v.Validate(new UpdateStudentRecordRequest("", new DateTime(2016, 1, 1), null, null, null, null)).IsValid);
        Assert.False(v.Validate(new UpdateStudentRecordRequest("Asha", DateTime.UtcNow.AddDays(1), null, null, null, null)).IsValid);
        Assert.False(v.Validate(new UpdateStudentRecordRequest("Asha", new DateTime(2016, 1, 1), null, new string('s', 21), null, null)).IsValid);
        Assert.False(v.Validate(new UpdateStudentRecordRequest("Asha", new DateTime(2016, 1, 1), null, null, null, (Gender)99)).IsValid);
    }
}
