using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using FluentValidation;

namespace ChildGuard.Application.Validators;

public class AddressDtoValidator : AbstractValidator<AddressDto>
{
    public AddressDtoValidator()
    {
        RuleFor(x => x.Line1).MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200);
        RuleFor(x => x.Locality).MaximumLength(100);
        RuleFor(x => x.Landmark).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.District).MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.PostalCode).MaximumLength(20)
            .Matches("^[A-Za-z0-9 -]*$").WithMessage("PIN / postal code may contain only letters, digits, spaces and '-'.");
        RuleFor(x => x.Country).MaximumLength(100);
    }
}

public class EnrollStudentRequestValidator : AbstractValidator<EnrollStudentRequest>
{
    public const int MaxGuardians = 4;

    public EnrollStudentRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(64)
            .Matches("^[A-Za-z0-9-]{8,64}$").WithMessage("IdempotencyKey must be 8-64 letters, digits or '-'.");

        RuleFor(x => x.Student).NotNull();
        RuleFor(x => x.Student.FullName).NotEmpty().MaximumLength(150).When(x => x.Student is not null);
        RuleFor(x => x.Student.DateOfBirth).NotEmpty().LessThan(DateTime.UtcNow)
            .WithMessage("Date of birth must be in the past.").When(x => x.Student is not null);
        RuleFor(x => x.Student.Grade).MaximumLength(50).When(x => x.Student is not null);
        RuleFor(x => x.Student.Section).MaximumLength(20).When(x => x.Student is not null);
        RuleFor(x => x.Student.AdmissionNumber).MaximumLength(50).When(x => x.Student is not null);
        RuleFor(x => x.Student.Gender).IsInEnum().When(x => x.Student?.Gender is not null);
        RuleFor(x => x.Student.Address!).SetValidator(new AddressDtoValidator()).When(x => x.Student?.Address is not null);

        RuleFor(x => x.Guardians).NotNull()
            .Must(g => g is { Count: >= 1 and <= MaxGuardians })
            .WithMessage($"Add between 1 and {MaxGuardians} guardians.");
        RuleForEach(x => x.Guardians).SetValidator(new EnrollmentGuardianDtoValidator());

        RuleFor(x => x.CardUid).MaximumLength(100);
        RuleFor(x => x.DuplicateStudentOverrideReason)
            .Must(r => r!.Trim().Length is >= 10 and <= 500)
            .When(x => !string.IsNullOrWhiteSpace(x.DuplicateStudentOverrideReason))
            .WithMessage("Give a reason of 10 to 500 characters for enrolling a student with the same name and date of birth.");
        RuleFor(x => x.Transport!.BusId).GreaterThan(0).When(x => x.Transport is not null);
        RuleFor(x => x.Transport!.RouteId).GreaterThan(0).When(x => x.Transport?.RouteId is not null);
    }
}

public class EnrollmentStudentCheckRequestValidator : AbstractValidator<EnrollmentStudentCheckRequest>
{
    public EnrollmentStudentCheckRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DateOfBirth).NotEmpty();
        RuleFor(x => x.AdmissionNumber).MaximumLength(50);
    }
}

public class EnrollmentGuardianDtoValidator : AbstractValidator<EnrollmentGuardianDto>
{
    public EnrollmentGuardianDtoValidator()
    {
        RuleFor(x => x.Relationship).MaximumLength(50);
        RuleFor(x => x.ExistingGuardianId).GreaterThan(0).When(x => x.ExistingGuardianId is not null);

        When(x => x.ExistingGuardianId is null, () =>
        {
            RuleFor(x => x.FullName).NotEmpty().WithMessage("Guardian name is required.").MaximumLength(150);
            RuleFor(x => x.Email).NotEmpty().WithMessage("Guardian email is required.").EmailAddress().MaximumLength(200);
            RuleFor(x => x.ContactNumber).NotEmpty().WithMessage("Guardian contact number is required.").MaximumLength(30)
                .Must(c => ContactNumber.Normalize(c) is { Length: >= 8 })
                .WithMessage("Enter a valid contact number.");
            RuleFor(x => x.Address!).SetValidator(new AddressDtoValidator()).When(x => x.Address is not null && !x.SameAsStudentAddress);
        });
    }
}
