using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Interfaces;

/// <summary>A generated file (template or error report).</summary>
public sealed record ImportFileDto(byte[] Data, string ContentType, string FileName);

/// <summary>
/// Bulk import: template → upload or manual rows → validated preview → transactional commit → history.
/// Nothing is written until <see cref="CommitAsync"/>, which validates every row again.
/// </summary>
public interface IBulkImportService
{
    IReadOnlyList<ImportCategoryDto> GetCategories();

    /// <param name="format">"xlsx" or "csv".</param>
    Result<ImportFileDto> GetTemplate(ImportCategory category, string format);

    /// <summary>Reads an uploaded .xlsx/.csv and validates its rows. Writes nothing.</summary>
    Task<Result<ImportPreviewDto>> PreviewFileAsync(ImportCategory category, byte[] content, string? fileName, CancellationToken ct = default);

    /// <summary>Validates rows from the grid (manual entry or a corrected preview). Writes nothing.</summary>
    Task<Result<ImportPreviewDto>> ValidateAsync(ValidateImportRequest request, CancellationToken ct = default);

    Task<Result<ImportSummaryDto>> CommitAsync(CommitImportRequest request, int adminId, CancellationToken ct = default);

    Task<List<ImportHistoryItemDto>> GetHistoryAsync(ImportCategory? category, CancellationToken ct = default);
    Task<Result<ImportDetailDto>> GetDetailAsync(int id, CancellationToken ct = default);

    /// <summary>The rows an import did not insert, with their values and reasons.</summary>
    Task<Result<ImportFileDto>> GetErrorReportAsync(int id, string format, CancellationToken ct = default);
}
