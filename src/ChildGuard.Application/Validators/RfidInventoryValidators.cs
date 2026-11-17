using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using FluentValidation;

namespace ChildGuard.Application.Validators;

internal static class RfidRules
{
    public static bool Reason(string? r) => r is not null && r.Trim().Length is >= 5 and <= 200;
    public const string ReasonMessage = "Give a reason of 5 to 200 characters.";
}

public class RegisterRfidCardRequestValidator : AbstractValidator<RegisterRfidCardRequest>
{
    public RegisterRfidCardRequestValidator() => RuleFor(x => x.CardUid).Must(u => !string.IsNullOrWhiteSpace(u) && u.Trim().Length <= 100)
        .WithMessage("Scan or type a card UID of up to 100 characters.");
}

public class AssignRegisteredCardRequestValidator : AbstractValidator<AssignRegisteredCardRequest>
{
    public AssignRegisteredCardRequestValidator() => RuleFor(x => x.StudentId).GreaterThan(0);
}

public class UnassignRfidRequestValidator : AbstractValidator<UnassignRfidRequest>
{
    public UnassignRfidRequestValidator()
    {
        RuleFor(x => x.Reason).Must(RfidRules.Reason).WithMessage(RfidRules.ReasonMessage);
        RuleFor(x => x.NewStatus).IsInEnum().When(x => x.NewStatus is not null);
    }
}

public class ReplaceRfidRequestValidator : AbstractValidator<ReplaceRfidRequest>
{
    public ReplaceRfidRequestValidator()
    {
        RuleFor(x => x.NewCardUid).Must(u => !string.IsNullOrWhiteSpace(u) && u.Trim().Length <= 100)
            .WithMessage("Scan or type the replacement card's UID (up to 100 characters).");
        RuleFor(x => x.OldCardStatus).IsInEnum();
        RuleFor(x => x.Reason).Must(RfidRules.Reason).WithMessage(RfidRules.ReasonMessage);
    }
}

public class ReassignRfidRequestValidator : AbstractValidator<ReassignRfidRequest>
{
    public ReassignRfidRequestValidator()
    {
        RuleFor(x => x.StudentId).GreaterThan(0);
        RuleFor(x => x.Reason).Must(RfidRules.Reason).WithMessage(RfidRules.ReasonMessage);
    }
}
