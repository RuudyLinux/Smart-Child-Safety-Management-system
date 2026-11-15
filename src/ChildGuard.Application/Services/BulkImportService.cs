using System.Text.Json;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Import;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class BulkImportService : IBulkImportService
{
    public const string CategoryNotSupported = "This import category is not supported.";
    public const string ImportNotFound = "Import not found.";
    private const int HistoryRows = 200;
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvContentType = "text/csv";

    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<ImportCategory, ImportHandler> _handlers;
    private readonly IBulkImportRepository _repo;
    private readonly SchoolClock _clock;

    public BulkImportService(IEnumerable<ImportHandler> handlers, IBulkImportRepository repo, SchoolClock clock)
    {
        _handlers = handlers.ToDictionary(h => h.Definition.Category);
        _repo = repo;
        _clock = clock;
    }

    public IReadOnlyList<ImportCategoryDto> GetCategories() =>
        ImportSchema.All.Where(d => _handlers.ContainsKey(d.Category)).Select(d => d.ToDto()).ToList();

    public Result<ImportFileDto> GetTemplate(ImportCategory category, string format)
    {
        var definition = ImportSchema.Find(category);
        if (definition is null || !_handlers.ContainsKey(category)) return Result<ImportFileDto>.Failure(CategoryNotSupported);

        var header = definition.Columns.Select(c => (string?)c.Header).ToList();
        var baseName = $"childguard-{definition.Name.ToLowerInvariant()}-template";

        if (IsCsv(format))
            return Result<ImportFileDto>.Success(new ImportFileDto(CsvFormat.Write(new[] { header }), CsvContentType, baseName + ".csv"));
        if (!IsXlsx(format))
            return Result<ImportFileDto>.Failure("Format must be xlsx or csv.");

        // Sheet 1 is what gets imported, so it carries the header only. The guidance lives on a second
        // sheet that the importer never reads, so an example row can never be imported by mistake.
        var guide = new List<IReadOnlyList<string?>> { new string?[] { "Column", "Required", "What to enter", "Example" } };
        guide.AddRange(definition.Columns.Select(c => (IReadOnlyList<string?>)new string?[]
        {
            c.Header, c.Required ? "Required" : "Optional",
            c.MaxLength is int max ? $"{c.Description} Up to {max} characters." : c.Description,
            c.Example
        }));
        guide.Add(new string?[] { });
        guide.Add(new string?[] { "Notes" });
        guide.AddRange(definition.Notes.Select(n => (IReadOnlyList<string?>)new string?[] { n }));
        guide.Add(new string?[] { $"Fill in the \"{definition.Name}\" sheet only, one record per row, at most {definition.MaxRows} rows. Do not use formulas." });

        var bytes = XlsxFormat.Write(new[]
        {
            new XlsxSheet(definition.Name, new[] { (IReadOnlyList<string?>)header }, definition.Columns.Select(c => (double)Math.Clamp(c.Header.Length + 8, 16, 34)).ToList()),
            new XlsxSheet("Instructions", guide, new double[] { 26, 12, 80, 28 })
        });
        return Result<ImportFileDto>.Success(new ImportFileDto(bytes, XlsxContentType, baseName + ".xlsx"));
    }

    public async Task<Result<ImportPreviewDto>> PreviewFileAsync(ImportCategory category, byte[] content, string? fileName, CancellationToken ct = default)
    {
        if (!_handlers.TryGetValue(category, out var handler)) return Result<ImportPreviewDto>.Failure(CategoryNotSupported);

        ImportFileContent file;
        try
        {
            file = ImportFileReader.Read(handler.Definition, content, fileName);
        }
        catch (ImportFileException ex)
        {
            return Result<ImportPreviewDto>.Failure(ex.Message);
        }

        var plans = await handler.PlanAsync(file.Rows, ct);
        return Result<ImportPreviewDto>.Success(Preview(category, file.Source, SafeFileName(fileName, file.Source), plans, file.Warnings));
    }

    public async Task<Result<ImportPreviewDto>> ValidateAsync(ValidateImportRequest request, CancellationToken ct = default)
    {
        if (!_handlers.TryGetValue(request.Category, out var handler)) return Result<ImportPreviewDto>.Failure(CategoryNotSupported);
        if (CheckRows(handler.Definition, request.Rows) is { } error) return Result<ImportPreviewDto>.Failure(error);

        var plans = await handler.PlanAsync(request.Rows, ct);
        return Result<ImportPreviewDto>.Success(Preview(request.Category, ImportSource.Manual, null, plans, new List<string>()));
    }

    public async Task<Result<ImportSummaryDto>> CommitAsync(CommitImportRequest request, int adminId, CancellationToken ct = default)
    {
        if (!_handlers.TryGetValue(request.Category, out var handler)) return Result<ImportSummaryDto>.Failure(CategoryNotSupported);
        if (!Enum.IsDefined(request.Source)) return Result<ImportSummaryDto>.Failure("Unknown import source.");
        if (CheckRows(handler.Definition, request.Rows) is { } error) return Result<ImportSummaryDto>.Failure(error);

        var startedAt = _clock.UtcNow;
        // Validated again here: the preview the browser holds is never trusted, and the database
        // may have changed since it was produced.
        var plans = await handler.PlanAsync(request.Rows, ct);
        var valid = plans.Where(p => p.IsValid).ToList();
        var invalid = plans.Where(p => !p.IsValid).ToList();

        var history = new DataImport
        {
            Category = request.Category,
            Source = request.Source,
            FileName = SafeFileName(request.FileName, request.Source),
            AdminId = adminId,
            StartedAt = startedAt,
            TotalRows = plans.Count
        };

        if (valid.Count == 0 || (invalid.Count > 0 && !request.SkipInvalidRows))
        {
            var reason = valid.Count == 0
                ? "No records were inserted: every row has errors."
                : $"No records were inserted: {invalid.Count} row(s) have errors and invalid rows were not to be skipped.";
            return Result<ImportSummaryDto>.Success(await RecordFailureAsync(history, plans, reason, ct));
        }

        var set = new ImportWriteSet();
        handler.Stage(valid, set, startedAt);

        history.InsertedRows = valid.Count;
        history.FailedRows = invalid.Count;
        history.Status = invalid.Count == 0 ? ImportStatus.Completed : ImportStatus.CompletedWithErrors;
        history.Message = invalid.Count == 0
            ? $"{valid.Count} {Noun(handler.Definition, valid.Count)} imported."
            : $"{valid.Count} {Noun(handler.Definition, valid.Count)} imported; {invalid.Count} row(s) skipped because of errors.";
        history.ErrorReportJson = invalid.Count == 0 ? null : SerializeRows(invalid);
        history.CompletedAt = _clock.UtcNow;

        var outcome = await _repo.SaveImportAsync(set, history, ct);
        if (outcome != ImportSaveOutcome.Saved)
        {
            var failed = new DataImport
            {
                Category = history.Category, Source = history.Source, FileName = history.FileName,
                AdminId = adminId, StartedAt = startedAt, TotalRows = plans.Count
            };
            return Result<ImportSummaryDto>.Success(await RecordFailureAsync(failed, plans, outcome == ImportSaveOutcome.CapacityExceeded
                ? "No records were inserted: a bus filled up while this import ran. Validate the rows again and retry."
                : "No records were inserted: some of these records were added by someone else while this import ran. Validate the rows again and retry.", ct));
        }

        var emailFailures = await handler.AfterCommitAsync(ct);
        if (emailFailures.Count > 0)
        {
            history.Message += $" {emailFailures.Count} credential email(s) could not be delivered; reissue those passwords from the guardian's page.";
            await _repo.UpdateHistoryAsync(history, ct);
        }

        return Result<ImportSummaryDto>.Success(new ImportSummaryDto(
            history.DataImportId, history.Category, history.Status, history.TotalRows, history.InsertedRows, history.FailedRows,
            history.Message!, invalid.Select(p => p.ToResult()).ToList(), emailFailures, history.StartedAt, history.CompletedAt));
    }

    public async Task<List<ImportHistoryItemDto>> GetHistoryAsync(ImportCategory? category, CancellationToken ct = default) =>
        (await _repo.GetHistoryAsync(category, HistoryRows, ct)).Select(ToHistoryItem).ToList();

    public async Task<Result<ImportDetailDto>> GetDetailAsync(int id, CancellationToken ct = default)
    {
        var record = await _repo.GetHistoryByIdAsync(id, ct);
        return record is null
            ? Result<ImportDetailDto>.Failure(ImportNotFound)
            : Result<ImportDetailDto>.Success(new ImportDetailDto(ToHistoryItem(record), DeserializeRows(record.ErrorReportJson)));
    }

    public async Task<Result<ImportFileDto>> GetErrorReportAsync(int id, string format, CancellationToken ct = default)
    {
        var record = await _repo.GetHistoryByIdAsync(id, ct);
        if (record is null) return Result<ImportFileDto>.Failure(ImportNotFound);
        var definition = ImportSchema.Find(record.Category);
        if (definition is null) return Result<ImportFileDto>.Failure(CategoryNotSupported);

        var rows = DeserializeRows(record.ErrorReportJson);
        var table = new List<IReadOnlyList<string?>>
        {
            new[] { "Row" }.Concat(definition.Columns.Select(c => c.Header)).Append("Errors").ToList<string?>()
        };
        foreach (var row in rows)
        {
            var errors = string.Join("; ", row.Errors.Select(e =>
                e.Column is null ? e.Message : $"{definition.Column(e.Column)?.Header ?? e.Column}: {e.Message}"));
            table.Add(new[] { row.RowNumber.ToString() }
                .Concat(definition.Columns.Select(c => row.Values.TryGetValue(c.Key, out var v) ? v : null))
                .Append(errors).ToList<string?>());
        }

        var baseName = $"childguard-import-{record.DataImportId}-errors";
        if (IsCsv(format))
            return Result<ImportFileDto>.Success(new ImportFileDto(CsvFormat.Write(table), CsvContentType, baseName + ".csv"));
        if (!IsXlsx(format))
            return Result<ImportFileDto>.Failure("Format must be xlsx or csv.");
        var widths = new double[] { 8 }.Concat(definition.Columns.Select(_ => 22.0)).Append(70).ToList();
        return Result<ImportFileDto>.Success(new ImportFileDto(
            XlsxFormat.Write(new[] { new XlsxSheet("Errors", table, widths) }), XlsxContentType, baseName + ".xlsx"));
    }

    // ------------------------------------------------------------------ helpers

    private async Task<ImportSummaryDto> RecordFailureAsync(DataImport history, List<RowPlan> plans, string reason, CancellationToken ct)
    {
        var invalid = plans.Where(p => !p.IsValid).ToList();
        history.InsertedRows = 0;
        history.FailedRows = plans.Count;
        history.Status = ImportStatus.Failed;
        history.Message = reason;
        // When every row was refused for one shared reason (a conflict), the valid rows are still worth
        // listing: they are what the admin needs to retry.
        history.ErrorReportJson = SerializeRows(invalid.Count > 0 ? invalid : plans);
        history.CompletedAt = _clock.UtcNow;
        await _repo.AddHistoryAsync(history, ct);
        return new ImportSummaryDto(history.DataImportId, history.Category, history.Status, history.TotalRows, 0, history.FailedRows,
            reason, invalid.Select(p => p.ToResult()).ToList(), new List<ImportEmailFailureDto>(), history.StartedAt, history.CompletedAt);
    }

    private static string? CheckRows(ImportCategoryDefinition definition, List<ImportRowDto>? rows)
    {
        if (rows is null || rows.Count == 0) return "Add at least one row.";
        if (rows.Count > definition.MaxRows) return $"{definition.Name} imports are limited to {definition.MaxRows} rows.";
        if (rows.Any(r => r is null || r.Values is null || r.RowNumber < 1)) return "Every row needs a row number and values.";
        if (rows.Select(r => r.RowNumber).Distinct().Count() != rows.Count) return "Row numbers must be unique.";
        if (rows.Any(r => r.Values.Count > ImportLimits.MaxColumns || r.Values.Values.Any(v => v is { Length: > ImportLimits.MaxCellLength })))
            return $"A row has too many values or a value longer than {ImportLimits.MaxCellLength} characters.";
        return null;
    }

    private static ImportPreviewDto Preview(ImportCategory category, ImportSource source, string? fileName, List<RowPlan> plans, List<string> warnings)
    {
        var valid = plans.Count(p => p.IsValid);
        return new ImportPreviewDto(category, source, fileName, plans.Count, valid, plans.Count - valid,
            plans.Select(p => p.ToResult()).ToList(), warnings);
    }

    private static ImportHistoryItemDto ToHistoryItem(DataImport d) => new(
        d.DataImportId, d.Category, d.Source, d.FileName, d.AdminId, d.Admin?.FullName ?? $"Admin #{d.AdminId}",
        d.StartedAt, d.CompletedAt, d.TotalRows, d.InsertedRows, d.FailedRows, d.Status, d.Message);

    private static string SerializeRows(IEnumerable<RowPlan> rows) =>
        JsonSerializer.Serialize(rows.Select(r => r.ToResult()).ToList(), ReportJson);

    private static List<ImportRowResultDto> DeserializeRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<ImportRowResultDto>();
        try
        {
            return JsonSerializer.Deserialize<List<ImportRowResultDto>>(json, ReportJson) ?? new List<ImportRowResultDto>();
        }
        catch (JsonException)
        {
            return new List<ImportRowResultDto>();
        }
    }

    /// <summary>Display name only: path parts and control characters removed, length capped.</summary>
    internal static string SafeFileName(string? fileName, ImportSource source)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) return source == ImportSource.Manual ? "Manual entry" : "Uploaded file";
        return name.Length > 200 ? name[..200] : name;
    }

    private static string Noun(ImportCategoryDefinition d, int count) =>
        count == 1 ? d.Name.TrimEnd('s').ToLowerInvariant() switch { "buse" => "bus", var s => s } : d.Name.ToLowerInvariant();

    private static bool IsCsv(string? format) => string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
    private static bool IsXlsx(string? format) => string.IsNullOrEmpty(format) || string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase);
}
