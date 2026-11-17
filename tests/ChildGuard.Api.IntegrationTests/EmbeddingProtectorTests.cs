using System.Security.Cryptography;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Infrastructure.FaceService;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>At-rest protection of face embeddings (no database needed).</summary>
public class EmbeddingProtectorTests
{
    private const string Embedding = "[0.123456,-0.5,0.75]";

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmEmbeddingProtector Protector(string? key = null, string id = "k1", Dictionary<string, string>? previous = null) =>
        new(new BiometricsOptions { EncryptionKey = key ?? NewKey(), EncryptionKeyId = id, PreviousKeys = previous ?? new() });

    [Fact]
    public void RoundTrips_AndTheStoredValueDoesNotContainTheEmbedding()
    {
        var p = Protector();
        var stored = p.Protect(Embedding, 7);

        Assert.StartsWith("enc:v1:k1:", stored);
        Assert.DoesNotContain("0.123456", stored);
        Assert.Equal(Embedding, p.Unprotect(stored, 7));
    }

    [Fact]
    public void ProtectingTheSameValueTwice_GivesDifferentCiphertext()
    {
        var p = Protector();
        Assert.NotEqual(p.Protect(Embedding, 7), p.Protect(Embedding, 7));
    }

    [Fact]
    public void ARowCopiedToAnotherGuardian_DoesNotDecrypt()
    {
        var p = Protector();
        var stored = p.Protect(Embedding, 7);

        Assert.Throws<EmbeddingProtectionException>(() => p.Unprotect(stored, 8));
    }

    [Fact]
    public void ATamperedValue_IsRefused()
    {
        var p = Protector();
        var stored = p.Protect(Embedding, 7);
        var bytes = Convert.FromBase64String(stored["enc:v1:k1:".Length..]);
        bytes[^1] ^= 0x01;
        var tampered = "enc:v1:k1:" + Convert.ToBase64String(bytes);

        Assert.Throws<EmbeddingProtectionException>(() => p.Unprotect(tampered, 7));
    }

    [Theory]
    [InlineData("enc:v1:k1:")]
    [InlineData("enc:v1:k1:AAAA")]
    [InlineData("enc:v1:k1:not base64 !!")]
    [InlineData("enc:v1:nocolon")]
    public void MalformedValues_AreRefused_NotCrashes(string stored)
    {
        Assert.Throws<EmbeddingProtectionException>(() => Protector().Unprotect(stored, 7));
    }

    [Fact]
    public void APlainRowFromBeforeEncryption_IsReturnedAsIs()
    {
        Assert.Equal(Embedding, Protector().Unprotect(Embedding, 7));
    }

    [Fact]
    public void AnotherKey_CannotRead()
    {
        var stored = Protector().Protect(Embedding, 7);
        Assert.Throws<EmbeddingProtectionException>(() => Protector().Unprotect(stored, 7));
    }

    [Fact]
    public void ARetiredKey_StillReadsOldValues_AndNewValuesUseTheActiveKey()
    {
        var oldKey = NewKey();
        var oldStored = Protector(oldKey, "k1").Protect(Embedding, 7);

        var rotated = Protector(NewKey(), "k2", new Dictionary<string, string> { ["k1"] = oldKey });

        Assert.Equal(Embedding, rotated.Unprotect(oldStored, 7));
        Assert.StartsWith("enc:v1:k2:", rotated.Protect(Embedding, 7));
    }

    [Fact]
    public void ARetiredKeyThatWasDropped_IsReportedClearly()
    {
        var stored = Protector(NewKey(), "k1").Protect(Embedding, 7);
        var ex = Assert.Throws<EmbeddingProtectionException>(() => Protector(NewKey(), "k2").Unprotect(stored, 7));
        Assert.Contains("no longer configured", ex.Message);
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("QUJD")] // 3 bytes
    public void AnInvalidKey_FailsAtStartup(string key)
    {
        Assert.Throws<InvalidOperationException>(() => Protector(key));
    }

    [Fact]
    public void AKeyIdWithAColon_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => Protector(id: "k:1"));
    }
}
