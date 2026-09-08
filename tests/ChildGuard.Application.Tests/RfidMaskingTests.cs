using ChildGuard.Application.Common;
using Xunit;

namespace ChildGuard.Application.Tests;

public class RfidMaskingTests
{
    [Theory]
    [InlineData("04A1B2C3A72F", "********A72F")]
    [InlineData("ABCDEFGH", "****EFGH")]
    public void Mask_HidesAllButLastFourCharacters(string cardUid, string expected)
    {
        Assert.Equal(expected, RfidMasking.Mask(cardUid));
    }

    [Theory]
    [InlineData("A72F", "****")]
    [InlineData("F", "*")]
    [InlineData("", "")]
    public void Mask_ShortUid_MasksEntirely(string cardUid, string expected)
    {
        Assert.Equal(expected, RfidMasking.Mask(cardUid));
    }
}
