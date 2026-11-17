using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace ChildGuard.Infrastructure.Persistence;

/// <summary>
/// Export, import and reset of all application data, driven by the EF model so every table is covered without a
/// hand-kept list. Administrator accounts are never touched. The order of tables comes from their foreign keys:
/// principals first when writing, dependents first when deleting, so no constraint is ever disabled.
///
/// The backup is provider-neutral JSON (one array of rows per table, entity property names), so a MySQL backup
/// restores into PostgreSQL and the other way round. Ids are kept, which keeps every reference intact.
/// </summary>
public sealed class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    public const string Format = "childguard-backup";
    public const int FormatVersion = 1;
    public const string ResetWord = "RESET";

    private readonly ChildGuardDbContext _db;
    private readonly IPasswordHasherService _hasher;
    private readonly SchoolClock _clock;
    private readonly IAuditTrail _audit;
    private readonly JsonSerializerOptions _json;

    public DatabaseMaintenanceService(ChildGuardDbContext db, IPasswordHasherService hasher, SchoolClock clock, IAuditTrail audit)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _audit = audit;
        _json = RowSerializerOptions(db.Model);
    }

    // ------------------------------------------------------------------ model

    /// <summary>Application tables (one entity type each), principals before dependents. Admins are left out.</summary>
    private IReadOnlyList<IEntityType> OrderedTypes()
    {
        var types = _db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.GetTableName() is not null && t.ClrType != typeof(Admin))
            .ToList();
        var ordered = new List<IEntityType>();
        var placed = new HashSet<IEntityType>();
        while (ordered.Count < types.Count)
        {
            var ready = types.Where(t => !placed.Contains(t) && t.GetForeignKeys()
                    .Select(fk => fk.PrincipalEntityType)
                    .All(p => p == t || placed.Contains(p) || !types.Contains(p)))
                .OrderBy(t => t.GetTableName(), StringComparer.Ordinal)
                .ToList();
            if (ready.Count == 0)
                throw new InvalidOperationException("The tables' foreign keys form a cycle; the maintenance order cannot be worked out.");
            foreach (var t in ready) { ordered.Add(t); placed.Add(t); }
        }
        return ordered;
    }

    private string Quote(string name) => _db.GetService<ISqlGenerationHelper>().DelimitIdentifier(name);

    private static string Table(IEntityType t) => t.GetTableName()!;

    private async Task<List<DatabaseTableCountDto>> CountsAsync(CancellationToken ct)
    {
        var counts = new List<DatabaseTableCountDto>();
        foreach (var t in OrderedTypes())
            counts.Add(new DatabaseTableCountDto(Table(t), await CountAsync(t, ct)));
        return counts.OrderBy(c => c.Table, StringComparer.Ordinal).ToList();
    }

    private Task<int> CountAsync(IEntityType t, CancellationToken ct) =>
        (Task<int>)typeof(DatabaseMaintenanceService).GetMethod(nameof(CountOf), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(t.ClrType).Invoke(this, [ct])!;

    private Task<int> CountOf<T>(CancellationToken ct) where T : class => _db.Set<T>().CountAsync(ct);

    // ------------------------------------------------------------------ overview

    public async Task<DatabaseOverviewDto> GetOverviewAsync(CancellationToken ct = default) => new(
        _db.Database.IsNpgsql() ? "PostgreSQL" : "MySQL",
        (await _db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault(),
        await _db.Admins.CountAsync(ct),
        await CountsAsync(ct));

    // ------------------------------------------------------------------ export

    public async Task<byte[]> ExportAsync(CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", FormatVersion);
            w.WriteString("exportedAtUtc", _clock.UtcNow);
            w.WriteString("provider", _db.Database.IsNpgsql() ? "PostgreSQL" : "MySQL");
            w.WriteString("latestMigration", (await _db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault());
            w.WriteString("note", "All ChildGuard application data. Administrator accounts are not included.");
            w.WritePropertyName("tables");
            w.WriteStartObject();
            foreach (var t in OrderedTypes())
            {
                w.WritePropertyName(Table(t));
                var rows = await LoadAllAsync(t, ct);
                JsonSerializer.Serialize(w, rows, rows.GetType(), _json);
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private Task<IList> LoadAllAsync(IEntityType t, CancellationToken ct) =>
        (Task<IList>)typeof(DatabaseMaintenanceService).GetMethod(nameof(LoadAll), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(t.ClrType).Invoke(this, [t, ct])!;

    private async Task<IList> LoadAll<T>(IEntityType t, CancellationToken ct) where T : class
    {
        var key = t.FindPrimaryKey()!.Properties[0].Name;
        return await _db.Set<T>().AsNoTracking().OrderBy(e => EF.Property<object>(e, key)).ToListAsync(ct);
    }

    // ------------------------------------------------------------------ import

    public async Task<Result<DatabaseOperationResultDto>> ImportAsync(Stream backup, CancellationToken ct = default)
    {
        JsonDocument doc;
        try { doc = await JsonDocument.ParseAsync(backup, cancellationToken: ct); }
        catch (JsonException) { return Result<DatabaseOperationResultDto>.Failure("This file is not a ChildGuard backup (it is not valid JSON)."); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format) || format.GetString() != Format
                || !root.TryGetProperty("tables", out var tables) || tables.ValueKind != JsonValueKind.Object)
                return Result<DatabaseOperationResultDto>.Failure("This file is not a ChildGuard backup. Use a file made with Export database.");
            if (!root.TryGetProperty("version", out var version) || version.GetInt32() > FormatVersion)
                return Result<DatabaseOperationResultDto>.Failure("This backup was made by a newer version of ChildGuard and cannot be imported here.");

            var types = OrderedTypes();
            var known = types.Select(Table).ToHashSet(StringComparer.Ordinal);
            var unknown = tables.EnumerateObject().Select(p => p.Name).Where(n => !known.Contains(n)).ToList();
            if (unknown.Count > 0)
                return Result<DatabaseOperationResultDto>.Failure($"The backup has tables this system does not know ({string.Join(", ", unknown)}).");

            // Parse everything before touching the database, so a bad file changes nothing.
            var data = new List<(IEntityType Type, IList Rows)>();
            try
            {
                foreach (var t in types)
                {
                    if (!tables.TryGetProperty(Table(t), out var rows)) continue;
                    var listType = typeof(List<>).MakeGenericType(t.ClrType);
                    data.Add((t, (IList)rows.Deserialize(listType, _json)!));
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                return Result<DatabaseOperationResultDto>.Failure($"The backup could not be read: {ex.Message}");
            }

            var admins = await _db.Admins.Select(a => a.AdminId).ToListAsync(ct);
            var adminSet = admins.ToHashSet();
            foreach (var (type, rows) in data) KeepAdminReferencesValid(type, rows, adminSet, admins.FirstOrDefault());

            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                await DeleteAllAsync(types, keep: _ => false, ct);
                foreach (var (_, rows) in data)
                    foreach (var row in rows) _db.Add(row);
                await _db.SaveChangesAsync(ct);
                _db.ChangeTracker.Clear();
                await tx.CommitAsync(ct);
            });
            await RealignIdentitiesAsync(types, ct);

            var total = data.Sum(d => d.Rows.Count);
            await _audit.RecordNowAsync(new AuditEvent("DatabaseImported", "Database", null,
                $"Database restored from a backup: {total} records")
            { Details = new { rows = total, exportedAtUtc = root.TryGetProperty("exportedAtUtc", out var at) ? at.GetString() : null } }, ct);

            return Result<DatabaseOperationResultDto>.Success(new DatabaseOperationResultDto(
                $"Backup imported: {total} records restored. Administrator accounts were not changed.", await CountsAsync(ct)));
        }
    }

    /// <summary>A backup from another installation can name admin ids that do not exist here; such references are moved to an existing admin, or cleared.</summary>
    private static void KeepAdminReferencesValid(IEntityType type, IList rows, HashSet<int> admins, int fallbackAdmin)
    {
        foreach (var fk in type.GetForeignKeys().Where(f => f.PrincipalEntityType.ClrType == typeof(Admin)))
        {
            var prop = fk.Properties[0].PropertyInfo;
            if (prop is null) continue;
            foreach (var row in rows)
            {
                if (prop.GetValue(row) is not int id || admins.Contains(id)) continue;
                prop.SetValue(row, fk.Properties[0].IsNullable ? null : fallbackAdmin);
            }
        }
    }

    // ------------------------------------------------------------------ reset

    public async Task<Result<DatabaseOperationResultDto>> ResetAsync(DatabaseResetRequest request, CancellationToken ct = default)
    {
        if (!string.Equals(request.Confirmation?.Trim(), ResetWord, StringComparison.Ordinal))
            return Result<DatabaseOperationResultDto>.Failure($"Type {ResetWord} to confirm the reset.");

        var types = OrderedTypes();
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            // Scanners are configuration (each has its own issued key), so they survive a reset.
            await DeleteAllAsync(types, keep: t => t.ClrType == typeof(RfidDevice), ct);
            await tx.CommitAsync(ct);
        });
        _db.ChangeTracker.Clear();
        await RealignIdentitiesAsync(types, ct);

        DemoSeedSummary? demo = null;
        if (request.LoadDemoData)
        {
            demo = await DemoDataSeeder.SeedIntoAsync(_db, _hasher, _clock, ct);
            _db.ChangeTracker.Clear();
        }

        await _audit.RecordNowAsync(new AuditEvent("DatabaseReset", "Database", null,
            demo is null ? "Database reset: all application data removed" : "Database reset and demo data loaded")
        { Details = new { demoData = demo } }, ct);

        var message = demo is null
            ? "Database reset. All application data was removed; administrator accounts and scanners were kept."
            : $"Database reset and demo data loaded: {demo.Students} students, {demo.Guardians} guardians, {demo.Drivers} drivers, " +
              $"{demo.Buses} buses on {demo.Routes} routes ({demo.Stops} stops). Administrator accounts were kept.";
        return Result<DatabaseOperationResultDto>.Success(new DatabaseOperationResultDto(message, await CountsAsync(ct)));
    }

    // ------------------------------------------------------------------ shared

    private async Task DeleteAllAsync(IReadOnlyList<IEntityType> types, Func<IEntityType, bool> keep, CancellationToken ct)
    {
        // Dependents first. Table names come from the EF model, never from input.
        foreach (var t in types.Reverse().Where(t => !keep(t)))
            await _db.Database.ExecuteSqlRawAsync($"DELETE FROM {Quote(Table(t))}", ct);
    }

    /// <summary>Points each table's id generator past its highest id (or back to 1 when empty).</summary>
    private async Task RealignIdentitiesAsync(IReadOnlyList<IEntityType> types, CancellationToken ct)
    {
        foreach (var t in types)
        {
            var key = t.FindPrimaryKey();
            if (key is not { Properties.Count: 1 } || key.Properties[0].ValueGenerated != ValueGenerated.OnAdd) continue;
            var table = Table(t);
            var column = key.Properties[0].GetColumnName(StoreObjectIdentifier.Table(table, t.GetSchema()))!;
            if (_db.Database.IsNpgsql())
            {
                var qualified = Quote(table).Replace("'", "''");
                await _db.Database.ExecuteSqlRawAsync(
                    $"SELECT setval(pg_get_serial_sequence('{qualified}', '{column}'), GREATEST(COALESCE(MAX({Quote(column)}), 0), 1), MAX({Quote(column)}) IS NOT NULL) FROM {Quote(table)}", ct);
            }
            else
            {
                // InnoDB raises a value below the current maximum to maximum + 1.
                await _db.Database.ExecuteSqlRawAsync($"ALTER TABLE {Quote(table)} AUTO_INCREMENT = 1", ct);
            }
        }
    }

    /// <summary>
    /// Rows are written with their column properties only: navigations are left out (the ids carry the
    /// relationships), and properties with non-public setters are still restored.
    /// </summary>
    private static JsonSerializerOptions RowSerializerOptions(IModel model)
    {
        var entityTypes = model.GetEntityTypes().Where(t => !t.IsOwned()).Select(t => t.ClrType).ToHashSet();
        bool IsNavigation(Type type) =>
            entityTypes.Any(e => e.IsAssignableFrom(type))
            || (type != typeof(string) && type != typeof(byte[]) && typeof(IEnumerable).IsAssignableFrom(type)
                && type.IsGenericType && entityTypes.Any(e => e.IsAssignableFrom(type.GetGenericArguments()[0])));

        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            for (var i = info.Properties.Count - 1; i >= 0; i--)
            {
                var p = info.Properties[i];
                if (IsNavigation(p.PropertyType)) { info.Properties.RemoveAt(i); continue; }
                if (p.Set is null && info.Type.GetProperty(p.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase) is { } clr
                    && clr.GetSetMethod(nonPublic: true) is { } setter)
                    p.Set = (obj, value) => setter.Invoke(obj, [value]);
            }
        });
        return new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
    }
}
