using ChildGuard.Application.DTOs;
using FluentValidation;

namespace ChildGuard.Application.Validators;

public class ArchiveRecordRequestValidator : AbstractValidator<ArchiveRecordRequest>
{
    public ArchiveRecordRequestValidator() =>
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 10 and <= 500)
            .WithMessage("Give a reason of 10 to 500 characters for archiving this record.");
}

public class DeleteRecordRequestValidator : AbstractValidator<DeleteRecordRequest>
{
    public DeleteRecordRequestValidator() =>
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 10 and <= 500)
            .WithMessage("Give a reason of 10 to 500 characters for permanently deleting this record.");
}

public class RestoreRecordRequestValidator : AbstractValidator<RestoreRecordRequest>
{
    public RestoreRecordRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}

public class UpdateStudentRecordRequestValidator : AbstractValidator<UpdateStudentRecordRequest>
{
    public UpdateStudentRecordRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DateOfBirth).NotEmpty().LessThan(DateTime.UtcNow).WithMessage("Date of birth must be in the past.");
        RuleFor(x => x.Grade).MaximumLength(50);
        RuleFor(x => x.Section).MaximumLength(20);
        RuleFor(x => x.AdmissionNumber).MaximumLength(50);
        RuleFor(x => x.Gender).IsInEnum().When(x => x.Gender is not null);
    }
}
