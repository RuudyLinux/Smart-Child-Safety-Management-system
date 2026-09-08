using ChildGuard.Application.DTOs;
using FluentValidation;

namespace ChildGuard.Application.Validators;

public class CreateStudentRequestValidator : AbstractValidator<CreateStudentRequest>
{
    public CreateStudentRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DateOfBirth).NotEmpty().LessThan(DateTime.UtcNow).WithMessage("Date of birth must be in the past.");
        RuleFor(x => x.Grade).MaximumLength(50);
    }
}

public class UpdateStudentRequestValidator : AbstractValidator<UpdateStudentRequest>
{
    public UpdateStudentRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DateOfBirth).NotEmpty().LessThan(DateTime.UtcNow).WithMessage("Date of birth must be in the past.");
        RuleFor(x => x.Grade).MaximumLength(50);
    }
}

public class AssignGuardianRequestValidator : AbstractValidator<AssignGuardianRequest>
{
    public AssignGuardianRequestValidator()
    {
        RuleFor(x => x.GuardianId).GreaterThan(0);
        RuleFor(x => x.Relationship).MaximumLength(50);
    }
}

public class AssignRfidRequestValidator : AbstractValidator<AssignRfidRequest>
{
    public AssignRfidRequestValidator()
    {
        RuleFor(x => x.CardUid).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StudentId).GreaterThan(0);
    }
}
