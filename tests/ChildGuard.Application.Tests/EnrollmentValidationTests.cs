using ChildGuard.Application.DTOs;
using ChildGuard.Application.Validators;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests;

public class EnrollmentValidationTests
{
    private static readonly EnrollmentStudentDto Student = new("Asha Rao", new DateTime(2016, 4, 12), "4", "B", null, Gender.Female, null);
    private static readonly EnrollmentGuardianDto NewGuardian = new(null, "Meera Rao", "meera@example.test", "98200 11111", null, false, "Mother", true);
    private static readonly EnrollmentGuardianDto Linked = new(42, null, null, null, null, false, "Father", true);

    private static bool Valid(EnrollStudentRequest r) => new EnrollStudentRequestValidator().Validate(r).IsValid;
    private static EnrollStudentRequest Request(params EnrollmentGuardianDto[] guardians) =>
        new(Guid.NewGuid().ToString("N"), Student, guardians, null, null);

    [Fact]
    public void NewAndExistingGuardians_AreAccepted() => Assert.True(Valid(Request(NewGuardian, Linked)));

    [Fact]
    public void ExistingGuardian_NeedsOnlyItsId() => Assert.True(Valid(Request(Linked)));

    [Fact]
    public void AtLeastOneAndAtMostFourGuardians()
    {
        Assert.False(Valid(Request()));
        Assert.False(Valid(Request(Linked, Linked with { ExistingGuardianId = 2 }, Linked with { ExistingGuardianId = 3 },
            Linked with { ExistingGuardianId = 4 }, Linked with { ExistingGuardianId = 5 })));
    }

    [Theory]
    [InlineData(null, "a@b.test", "9820011111")]
    [InlineData("Name", null, "9820011111")]
    [InlineData("Name", "not-an-email", "9820011111")]
    [InlineData("Name", "a@b.test", null)]
    [InlineData("Name", "a@b.test", "12345")]
    public void NewGuardian_NeedsNameEmailAndContact(string? name, string? email, string? contact) =>
        Assert.False(Valid(Request(NewGuardian with { FullName = name, Email = email, ContactNumber = contact })));

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces in it")]
    [InlineData("semi;colon-key-0001")]
    public void IdempotencyKey_MustBeAnOpaqueToken(string key) =>
        Assert.False(Valid(Request(Linked) with { IdempotencyKey = key }));

    [Fact]
    public void FutureDateOfBirth_IsRejected() =>
        Assert.False(Valid(Request(Linked) with { Student = Student with { DateOfBirth = DateTime.UtcNow.AddDays(2) } }));

    [Fact]
    public void PostalCode_IsChecked() =>
        Assert.False(Valid(Request(Linked) with
        {
            Student = Student with { Address = new AddressDto(null, null, null, null, null, null, null, "400<050>", null) }
        }));

    [Fact]
    public void Transport_NeedsABus() =>
        Assert.False(Valid(Request(Linked) with { Transport = new EnrollmentTransportDto(0, null) }));

    [Fact]
    public void BlankAddress_BecomesNoAddress_AndValuesAreTrimmed()
    {
        Assert.Null(new AddressDto(" ", null, "", null, null, null, null, null, "  ").ToEntity());
        var a = new AddressDto("  12 Lake View Rd ", null, null, null, " Mumbai", null, null, " 400050 ", null).ToEntity()!;
        Assert.Equal(("12 Lake View Rd", "Mumbai", "400050"), (a.Line1, a.City, a.PostalCode));
        Assert.Null(a.Line2);
    }
}
