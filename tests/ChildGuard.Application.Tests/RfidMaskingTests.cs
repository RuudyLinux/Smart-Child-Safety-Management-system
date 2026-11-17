using ChildGuard.Application.Common;

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

public class EmailMaskingTests
{
    [Theory]
    [InlineData("priya.patel@gmail.com", "p***@gmail.com")]
    [InlineData("a@x.io", "a***@x.io")]
    [InlineData("first.last+tag@sub.example.org", "f***@sub.example.org")]
    public void ShowsTheFirstLetterAndTheDomain_NothingElseOfTheName(string email, string expected)
    {
        Assert.Equal(expected, ChildGuard.Application.Common.EmailMasking.Mask(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@nodomain-name.com")]
    [InlineData("name@")]
    public void UnusableAddresses_GiveNoHint(string? email)
    {
        Assert.Null(ChildGuard.Application.Common.EmailMasking.Mask(email));
    }
}
