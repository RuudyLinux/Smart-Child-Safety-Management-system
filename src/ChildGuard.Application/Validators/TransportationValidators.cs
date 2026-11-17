using ChildGuard.Application.DTOs;
using FluentValidation;

namespace ChildGuard.Application.Validators;

public class CreateDriverRequestValidator : AbstractValidator<CreateDriverRequest>
{
    public CreateDriverRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.LicenseNumber).MaximumLength(50);
    }
}

public class CreateRouteRequestValidator : AbstractValidator<CreateRouteRequest>
{
    public CreateRouteRequestValidator()
    {
        RuleFor(x => x.RouteName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class CreateBusRequestValidator : AbstractValidator<CreateBusRequest>
{
    public CreateBusRequestValidator()
    {
        RuleFor(x => x.BusNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Capacity).GreaterThan(0).LessThanOrEqualTo(200);
        RuleFor(x => x.DriverId).GreaterThan(0).When(x => x.DriverId.HasValue);
        RuleFor(x => x.RouteId).GreaterThan(0).When(x => x.RouteId.HasValue);
    }
}

public class CreateStudentBusAssignmentRequestValidator : AbstractValidator<CreateStudentBusAssignmentRequest>
{
    public CreateStudentBusAssignmentRequestValidator()
    {
        RuleFor(x => x.StudentId).GreaterThan(0);
        RuleFor(x => x.BusId).GreaterThan(0);
        RuleFor(x => x.RouteId).GreaterThan(0).When(x => x.RouteId.HasValue);
        RuleFor(x => x.PickupStopId).GreaterThan(0).When(x => x.PickupStopId.HasValue);
        RuleFor(x => x.DropStopId).GreaterThan(0).When(x => x.DropStopId.HasValue);
    }
}

public class UpdateDriverRequestValidator : AbstractValidator<UpdateDriverRequest>
{
    public UpdateDriverRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.LicenseNumber).MaximumLength(50);
    }
}

public class UpdateRouteRequestValidator : AbstractValidator<UpdateRouteRequest>
{
    public UpdateRouteRequestValidator()
    {
        RuleFor(x => x.RouteName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class UpdateBusRequestValidator : AbstractValidator<UpdateBusRequest>
{
    public UpdateBusRequestValidator()
    {
        RuleFor(x => x.BusNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Capacity).GreaterThan(0).LessThanOrEqualTo(200);
        RuleFor(x => x.DriverId).GreaterThan(0).When(x => x.DriverId.HasValue);
        RuleFor(x => x.RouteId).GreaterThan(0).When(x => x.RouteId.HasValue);
    }
}

public class UpdateTransportStatusRequestValidator : AbstractValidator<UpdateTransportStatusRequest>
{
    public UpdateTransportStatusRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}
