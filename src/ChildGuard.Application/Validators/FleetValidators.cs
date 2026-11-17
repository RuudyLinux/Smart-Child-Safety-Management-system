using ChildGuard.Application.DTOs;
using FluentValidation;

namespace ChildGuard.Application.Validators;

public class UpdateDriverProfileRequestValidator : AbstractValidator<UpdateDriverProfileRequest>
{
    public UpdateDriverProfileRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.LicenseNumber).MaximumLength(50);
        RuleFor(x => x.EmployeeCode).MaximumLength(30);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address!).SetValidator(new AddressDtoValidator()).When(x => x.Address is not null);
        RuleFor(x => x.EmergencyContactName).MaximumLength(150);
        RuleFor(x => x.EmergencyContactPhone).MaximumLength(30);
        RuleFor(x => x.LicenseCategory).MaximumLength(30);
        RuleFor(x => x.LicenseExpiresOn).Must(d => d is null || d.Value.Year is >= 1990 and <= 2100)
            .WithMessage("Enter a realistic licence expiry date.");
        RuleFor(x => x.JoinedOn).Must(d => d is null || d.Value <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            .WithMessage("The joining date cannot be in the future.");
    }
}

public class UpdateBusDetailsRequestValidator : AbstractValidator<UpdateBusDetailsRequest>
{
    public UpdateBusDetailsRequestValidator()
    {
        RuleFor(x => x.RegistrationNumber).MaximumLength(20)
            .Matches("^[A-Za-z0-9 -]*$").WithMessage("A registration number may contain only letters, digits, spaces and '-'.");
        RuleFor(x => x.Manufacturer).MaximumLength(60);
        RuleFor(x => x.Model).MaximumLength(60);
    }
}

public class BusOperationalStatusRequestValidator : AbstractValidator<BusOperationalStatusRequest>
{
    public BusOperationalStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(200);
    }
}

public class RouteStopRequestValidator : AbstractValidator<RouteStopRequest>
{
    public RouteStopRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Address).MaximumLength(200);
    }
}

public class ReorderStopsRequestValidator : AbstractValidator<ReorderStopsRequest>
{
    public ReorderStopsRequestValidator() => RuleFor(x => x.StopIds).NotNull().Must(s => s.Count <= 200);
}

public class UpdateRouteDetailsRequestValidator : AbstractValidator<UpdateRouteDetailsRequest>
{
    public UpdateRouteDetailsRequestValidator()
    {
        RuleFor(x => x.StartPoint).MaximumLength(150);
        RuleFor(x => x.Destination).MaximumLength(150);
    }
}

public class UpdateAssignmentStopsRequestValidator : AbstractValidator<UpdateAssignmentStopsRequest>
{
    public UpdateAssignmentStopsRequestValidator()
    {
        RuleFor(x => x.PickupStopId).GreaterThan(0).When(x => x.PickupStopId.HasValue);
        RuleFor(x => x.DropStopId).GreaterThan(0).When(x => x.DropStopId.HasValue);
    }
}
