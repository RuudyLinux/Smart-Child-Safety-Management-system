using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default);
}
