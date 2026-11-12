using ChildGuard.Domain.Enums;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface ITransportationService
{
    Task<List<DriverDto>> GetDriversAsync(CancellationToken ct = default);
    Task<DriverDto> AddDriverAsync(CreateDriverRequest request, CancellationToken ct = default);

    Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default);
    Task<Result<RouteDto>> AddRouteAsync(CreateRouteRequest request, CancellationToken ct = default);

    Task<List<BusDto>> GetBusesAsync(CancellationToken ct = default);
    Task<Result<BusDto>> AddBusAsync(CreateBusRequest request, CancellationToken ct = default);

    Task<List<StudentBusAssignmentDto>> GetAssignmentsAsync(CancellationToken ct = default);
    Task<Result> AddAssignmentAsync(CreateStudentBusAssignmentRequest request, CancellationToken ct = default);

    // Edit and status changes. Nothing is deleted: history keeps referring to the
    // same rows, which become Inactive instead.
    Task<Result> UpdateDriverAsync(int id, UpdateDriverRequest request, CancellationToken ct = default);
    Task<Result> SetDriverStatusAsync(int id, RecordStatus status, CancellationToken ct = default);
    Task<Result> UpdateRouteAsync(int id, UpdateRouteRequest request, CancellationToken ct = default);
    Task<Result> SetRouteStatusAsync(int id, RecordStatus status, CancellationToken ct = default);
    Task<Result> UpdateBusAsync(int id, UpdateBusRequest request, CancellationToken ct = default);
    Task<Result> SetBusStatusAsync(int id, RecordStatus status, CancellationToken ct = default);
    Task<Result> SetAssignmentStatusAsync(int id, RecordStatus status, CancellationToken ct = default);
}
