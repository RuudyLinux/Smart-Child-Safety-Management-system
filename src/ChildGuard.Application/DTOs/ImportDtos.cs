using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

/// <summary>One column a bulk-import category accepts. <paramref name="Key"/> is the stable JSON key of a row value.</summary>
public record ImportColumnDto(string Key, string Header, bool Required, string Description, string? Example, int? MaxLength);

/// <summary>A category that can be bulk-imported, with the columns its template carries.</summary>
public record ImportCategoryDto(
    ImportCategory Category, string Name, string Description, int MaxRows,
    IReadOnlyList<ImportColumnDto> Columns, IReadOnlyList<string> Notes);

/// <summary>
/// One row as supplied by a file or the manual-entry grid. <paramref name="RowNumber"/> is the
/// spreadsheet row (the header is row 1) or the grid position, so errors point at what the admin sees.
/// Values are keyed by <see cref="ImportColumnDto.Key"/>; unknown keys are ignored.
/// </summary>
public record ImportRowDto(int RowNumber, Dictionary<string, string?> Values);

public record ImportFieldErrorDto(string? Column, string Message);

public record ImportRowResultDto(
    int RowNumber, Dictionary<string, string?> Values, bool IsValid, IReadOnlyList<ImportFieldErrorDto> Errors);

/// <summary>Rows (from a file or the grid) to check without writing anything.</summary>
public record ValidateImportRequest(ImportCategory Category, List<ImportRowDto> Rows);

/// <summary>Validation of every row, before anything is inserted.</summary>
public record ImportPreviewDto(
    ImportCategory Category, ImportSource Source, string? FileName,
    int TotalRows, int ValidRows, int InvalidRows,
    IReadOnlyList<ImportRowResultDto> Rows,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Inserts the rows. They are validated again on the server, so the preview the browser holds
/// is never trusted. With <paramref name="SkipInvalidRows"/> the valid rows are inserted and the
/// rest reported; without it any invalid row cancels the whole import. Either way the insert is
/// a single transaction.
/// </summary>
public record CommitImportRequest(
    ImportCategory Category, ImportSource Source, string? FileName, List<ImportRowDto> Rows, bool SkipInvalidRows = true);

/// <summary>A credential email that could not be delivered after a guardian import.</summary>
public record ImportEmailFailureDto(int RowNumber, string FullName, string Email, string? Error);

public record ImportSummaryDto(
    int DataImportId, ImportCategory Category, ImportStatus Status,
    int TotalRows, int InsertedRows, int FailedRows, string Message,
    IReadOnlyList<ImportRowResultDto> FailedRowDetails,
    IReadOnlyList<ImportEmailFailureDto> EmailFailures,
    DateTime StartedAt, DateTime CompletedAt);

public record ImportHistoryItemDto(
    int DataImportId, ImportCategory Category, ImportSource Source, string FileName,
    int AdminId, string AdminName, DateTime StartedAt, DateTime CompletedAt,
    int TotalRows, int InsertedRows, int FailedRows, ImportStatus Status, string? Message);

public record ImportDetailDto(ImportHistoryItemDto Import, IReadOnlyList<ImportRowResultDto> FailedRows);
