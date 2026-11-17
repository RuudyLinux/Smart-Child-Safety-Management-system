using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

/// <summary>
/// Whole-database maintenance for administrators: export every application record to one JSON backup file, restore
/// such a file, and reset the data (optionally loading the demo school). Administrator accounts are never exported,
/// imported or removed, so the signed-in admin keeps the same id, email and password through all three.
/// </summary>
public interface IDatabaseMaintenanceService
{
    Task<DatabaseOverviewDto> GetOverviewAsync(CancellationToken ct = default);

    /// <summary>The backup file's bytes (UTF-8 JSON).</summary>
    Task<byte[]> ExportAsync(CancellationToken ct = default);

    /// <summary>Replaces all application data with the backup's, in one transaction: a failure leaves the database as it was.</summary>
    Task<Result<DatabaseOperationResultDto>> ImportAsync(Stream backup, CancellationToken ct = default);

    /// <summary>Removes all application data (scanners are kept), then optionally loads the demo school.</summary>
    Task<Result<DatabaseOperationResultDto>> ResetAsync(DatabaseResetRequest request, CancellationToken ct = default);
}
