extern alias AdminWebAssembly;
using AdminWebAssembly::ChildGuard.AdminWeb.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// AdminWeb's cookies and antiforgery tokens must survive a restart on a host whose disk is wiped
/// (Render free). The key ring comes from configuration instead (no database needed).
/// </summary>
public class AdminWebKeyRingTests
{
    /// <summary>Makes a real key ring the way tools/KeyRingGenerator does.</summary>
    private static string NewKeyRing()
    {
        var folder = Directory.CreateTempSubdirectory("childguard-keyring-test-");
        try
        {
            var provider = DataProtectionProvider.Create(folder, o =>
            {
                o.SetApplicationName("ChildGuard.AdminWeb");
                o.SetDefaultKeyLifetime(TimeSpan.FromDays(3650));
            });
            provider.CreateProtector("seed").Protect("x");
            return File.ReadAllText(folder.GetFiles("key-*.xml").Single().FullName);
        }
        finally { folder.Delete(true); }
    }

    private static IDataProtector ProtectorFromConfiguration(string keyRingXml)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("ChildGuard.AdminWeb")
            .AddKeyManagementOptions(o => o.XmlRepository = new ConfiguredKeyRingRepository(keyRingXml));
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie");
    }

    [Fact]
    public void ProtectedData_SurvivesARestart_BecauseTheKeysComeFromConfiguration()
    {
        var keyRing = NewKeyRing();

        var before = ProtectorFromConfiguration(keyRing).Protect("signed-in admin");
        // A new process: nothing on disk, same configuration.
        var after = ProtectorFromConfiguration(keyRing).Unprotect(before);

        Assert.Equal("signed-in admin", after);
    }

    [Fact]
    public void ProtectedData_FromAnotherKeyRing_IsRejected()
    {
        var token = ProtectorFromConfiguration(NewKeyRing()).Protect("signed-in admin");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => ProtectorFromConfiguration(NewKeyRing()).Unprotect(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<key><unclosed>")]
    [InlineData("<notakey/>")]
    public void AnInvalidKeyRing_StopsStartup_WithoutQuotingIt(string keyRing)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ConfiguredKeyRingRepository(keyRing));
        if (keyRing.Trim().Length > 0) Assert.DoesNotContain(keyRing.Trim(), ex.Message);
    }

    [Fact]
    public void TheGeneratedKeyRing_HasOneKeyElement()
    {
        var elements = new ConfiguredKeyRingRepository(NewKeyRing()).GetAllElements();
        Assert.Single(elements);
        Assert.Equal("key", elements.Single().Name.LocalName);
    }
}
