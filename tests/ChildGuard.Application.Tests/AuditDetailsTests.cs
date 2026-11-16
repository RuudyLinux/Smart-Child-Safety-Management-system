using ChildGuard.Application.Common;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Tests;

public class AuditDetailsTests
{
    [Fact]
    public void SensitiveValues_AreRedacted_AtAnyDepth()
    {
        var json = AuditDetails.Serialize(new
        {
            email = "a@b.test",
            password = "P@ss-1",
            nested = new { passwordHash = "AQAAAA==", resetToken = "tok", apiKey = "key", embeddingJson = "[0.1]", imageBase64 = "aW1n" },
            list = new[] { new { otpCode = "123456", postalCode = "560001" } },
            fcmToken = "fcm",
            resetTicket = "ticket"
        })!;

        foreach (var secret in new[] { "P@ss-1", "AQAAAA==", "\"tok\"", "\"key\"", "[0.1]", "aW1n", "123456", "\"fcm\"", "\"ticket\"" })
            Assert.DoesNotContain(secret, json);
        Assert.Contains("a@b.test", json);
        Assert.Contains("560001", json);
        Assert.Contains(AuditDetails.Redacted, json);
    }

    [Theory]
    [InlineData("password", true)]
    [InlineData("NewPassword", true)]
    [InlineData("ApiKeyHash", true)]
    [InlineData("embedding", true)]
    [InlineData("imageBase64", true)]
    [InlineData("challengeToken", true)]
    [InlineData("postalCode", false)]
    [InlineData("contactNumber", false)]
    [InlineData("relationship", false)]
    public void SensitiveNames_AreRecognised(string name, bool sensitive) => Assert.Equal(sensitive, AuditDetails.IsSensitiveName(name));

    [Fact]
    public void OversizedDetails_AreReplacedByANote_NotCutIntoInvalidJson()
    {
        var json = AuditDetails.Serialize(new { text = new string('x', AuditDetails.MaxLength + 10) })!;
        Assert.True(json.Length < AuditDetails.MaxLength);
        Assert.Contains("omitted", json);
        Assert.Null(AuditDetails.Serialize(null));
    }

    [Fact]
    public void Changes_ListOnlyFieldsThatChanged()
    {
        var changes = AuditDetails.Changes(("name", "A", "A"), ("grade", "2B", "3A"), ("section", null, "C"));
        Assert.Equal(new[] { "grade", "section" }, changes.Keys);
        Assert.Contains("\"grade\":{\"from\":\"2B\",\"to\":\"3A\"}", AuditDetails.Serialize(changes));
    }

    [Fact]
    public void ChangedAddressParts_NamePartsWithoutValues()
    {
        var before = new PostalAddress { Line1 = "1 Main St", City = "Pune" };
        var after = new PostalAddress { Line1 = "2 Main St", City = "Pune", PostalCode = "411001" };
        Assert.Equal(new[] { "line1", "postalCode" }, AuditDetails.ChangedAddressParts(before, after));
        Assert.Equal(new[] { "line1", "city" }, AuditDetails.ChangedAddressParts(before, null));
        Assert.Empty(AuditDetails.ChangedAddressParts(null, null));
    }
}
