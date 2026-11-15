using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Interfaces;

public sealed record GuardianImportKey(int GuardianId, string Email, string? Phone);
public sealed record StudentImportKey(string FullName, DateTime DateOfBirth);
public sealed record DriverImportKey(int DriverId, string FullName, string? Phone, string? LicenseNumber, RecordStatus Status = RecordStatus.Active);
public sealed record RouteImportKey(int RouteId, string RouteName, RecordStatus Status = RecordStatus.Active);
/// <param name="ActiveStudents">Seats already taken by active assignments.</param>
public sealed record BusImportKey(int BusId, string BusNumber, int? RouteId, int Capacity = int.MaxValue, RecordStatus Status = RecordStatus.Active, int ActiveStudents = 0);

/// <summary>Outcome of saving an import.</summary>
public enum ImportSaveOutcome
{
    Saved,
    /// <summary>A unique index refused a row (someone else added the same record meanwhile).</summary>
    Conflict,
    /// <summary>A bus filled up meanwhile, so the new assignments no longer fit.</summary>
    CapacityExceeded
}

/// <summary>Everything one import inserts. Saved together, in one transaction, with its history row.</summary>
public sealed class ImportWriteSet
{
    public List<Student> Students { get; } = new();
    public List<Guardian> Guardians { get; } = new();
    public List<Driver> Drivers { get; } = new();
    public List<Route> Routes { get; } = new();
    public List<Bus> Buses { get; } = new();

    public int Count => Students.Count + Guardians.Count + Drivers.Count + Routes.Count + Buses.Count;
}

/// <summary>
/// Lookups used to detect duplicates and resolve references, and the single transactional write
/// of an import. Lookups are untracked projections of the natural keys only.
/// </summary>
public interface IBulkImportRepository
{
    Task<List<GuardianImportKey>> GetGuardianKeysAsync(CancellationToken ct = default);
    Task<List<StudentImportKey>> GetStudentKeysAsync(CancellationToken ct = default);
    Task<List<string>> GetRfidCardUidsAsync(CancellationToken ct = default);
    Task<List<DriverImportKey>> GetDriverKeysAsync(CancellationToken ct = default);
    Task<List<RouteImportKey>> GetRouteKeysAsync(CancellationToken ct = default);
    Task<List<BusImportKey>> GetBusKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts the records and the history row in one transaction. Buses that receive new student
    /// assignments are row-locked and their free seats checked again first. Anything but
    /// <see cref="ImportSaveOutcome.Saved"/> means nothing was written.
    /// </summary>
    Task<ImportSaveOutcome> SaveImportAsync(ImportWriteSet records, DataImport history, CancellationToken ct = default);

    /// <summary>Records an import that inserted nothing.</summary>
    Task AddHistoryAsync(DataImport history, CancellationToken ct = default);

    /// <summary>Saves later changes to a history row already written (e.g. undelivered emails).</summary>
    Task UpdateHistoryAsync(DataImport history, CancellationToken ct = default);

    Task<List<DataImport>> GetHistoryAsync(ImportCategory? category, int take, CancellationToken ct = default);
    Task<DataImport?> GetHistoryByIdAsync(int id, CancellationToken ct = default);
}
