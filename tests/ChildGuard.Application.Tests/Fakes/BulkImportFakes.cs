using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests.Fakes;

internal sealed class FakeBulkImportRepository : IBulkImportRepository
{
    public List<GuardianImportKey> Guardians { get; } = new();
    public List<StudentImportKey> Students { get; } = new();
    public List<string> CardUids { get; } = new();
    public List<DriverImportKey> Drivers { get; } = new();
    public List<RouteImportKey> Routes { get; } = new();
    public List<BusImportKey> Buses { get; } = new();
    public List<RouteStopImportKey> RouteStopKeys { get; } = new();
    public List<CalendarDayImportKey> CalendarDayKeys { get; } = new();

    public List<DataImport> History { get; } = new();
    public List<ImportWriteSet> Saved { get; } = new();
    /// <summary>Simulates a unique index refusing the batch.</summary>
    public bool FailNextSaveWithConflict { get; set; }
    /// <summary>Simulates a bus filling up between validation and save.</summary>
    public bool FailNextSaveWithCapacity { get; set; }
    public int Updates { get; private set; }
    private int _nextId = 1;

    public Task<List<GuardianImportKey>> GetGuardianKeysAsync(CancellationToken ct = default) => Task.FromResult(Guardians.ToList());
    public Task<List<StudentImportKey>> GetStudentKeysAsync(CancellationToken ct = default) => Task.FromResult(Students.ToList());
    public Task<List<string>> GetRfidCardUidsAsync(CancellationToken ct = default) => Task.FromResult(CardUids.ToList());
    public Task<List<DriverImportKey>> GetDriverKeysAsync(CancellationToken ct = default) => Task.FromResult(Drivers.ToList());
    public Task<List<RouteImportKey>> GetRouteKeysAsync(CancellationToken ct = default) => Task.FromResult(Routes.ToList());
    public Task<List<BusImportKey>> GetBusKeysAsync(CancellationToken ct = default) => Task.FromResult(Buses.ToList());
    public Task<List<RouteStopImportKey>> GetRouteStopKeysAsync(CancellationToken ct = default) => Task.FromResult(RouteStopKeys.ToList());
    public Task<List<CalendarDayImportKey>> GetCalendarDayKeysAsync(CancellationToken ct = default) => Task.FromResult(CalendarDayKeys.ToList());

    public Task<ImportSaveOutcome> SaveImportAsync(ImportWriteSet records, DataImport history, CancellationToken ct = default)
    {
        if (FailNextSaveWithConflict)
        {
            FailNextSaveWithConflict = false;
            return Task.FromResult(ImportSaveOutcome.Conflict);
        }
        if (FailNextSaveWithCapacity)
        {
            FailNextSaveWithCapacity = false;
            return Task.FromResult(ImportSaveOutcome.CapacityExceeded);
        }
        Saved.Add(records);
        history.DataImportId = _nextId++;
        History.Add(history);
        return Task.FromResult(ImportSaveOutcome.Saved);
    }

    public Task AddHistoryAsync(DataImport history, CancellationToken ct = default)
    {
        history.DataImportId = _nextId++;
        History.Add(history);
        return Task.CompletedTask;
    }

    public Task UpdateHistoryAsync(DataImport history, CancellationToken ct = default)
    {
        Updates++;
        return Task.CompletedTask;
    }

    public Task<List<DataImport>> GetHistoryAsync(ImportCategory? category, int take, CancellationToken ct = default) =>
        Task.FromResult(History.Where(h => category is null || h.Category == category).OrderByDescending(h => h.StartedAt).Take(take).ToList());

    public Task<DataImport?> GetHistoryByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(History.FirstOrDefault(h => h.DataImportId == id));
}
