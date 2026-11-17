using ChildGuard.Application.Common;

namespace ChildGuard.Application.Tests;

public class AdmissionNumbersTests
{
    [Fact]
    public void Number_IsYearAndPaddedId()
    {
        Assert.Equal("ADM202600042", AdmissionNumbers.For(42, new DateOnly(2026, 11, 16)));
        Assert.Equal("ADM2027123456", AdmissionNumbers.For(123456, new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public async Task NumberAlreadyEnteredByHand_GetsASuffix()
    {
        var taken = new HashSet<string> { "ADM202600007", "ADM202600007-2" };
        var number = await AdmissionNumbers.NextFreeAsync(7, new DateOnly(2026, 6, 1), n => Task.FromResult(taken.Contains(n)));
        Assert.Equal("ADM202600007-3", number);
    }

    [Fact]
    public async Task FreeNumber_IsUsedAsIs()
    {
        var number = await AdmissionNumbers.NextFreeAsync(7, new DateOnly(2026, 6, 1), _ => Task.FromResult(false));
        Assert.Equal("ADM202600007", number);
    }
}
