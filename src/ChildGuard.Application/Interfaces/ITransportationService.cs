using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface ITransportationService
{
    Task<List<DriverDto>> GetDriversAsync(CancellationToken ct = default);
    Task<DriverDto> AddDriverAsync(CreateDriverRequest request, CancellationToken ct = default);

    Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default);
    Task<RouteDto> AddRouteAsync(CreateRouteRequest request, CancellationToken ct = default);

    Task<List<BusDto>> GetBusesAsync(CancellationToken ct = default);
    Task<Result<BusDto>> AddBusAsync(CreateBusRequest request, CancellationToken ct = default);

    Task<List<StudentBusAssignmentDto>> GetAssignmentsAsync(CancellationToken ct = default);
    Task<Result> AddAssignmentAsync(CreateStudentBusAssignmentRequest request, CancellationToken ct = default);
}
