using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

/// <summary>
/// One bulk import run by an admin: the import history row and its audit record in one.
/// Written in the same database transaction as the records it inserted, so history never
/// claims rows that were not saved. A run that failed as a whole is recorded too.
/// </summary>
public class DataImport
{
    public int DataImportId { get; set; }
    public ImportCategory Category { get; set; }
    public ImportSource Source { get; set; }
    /// <summary>Name of the uploaded file as the admin's browser sent it (display only), or a label for manual entry.</summary>
    public string FileName { get; set; } = default!;
    public int AdminId { get; set; }
    public Admin Admin { get; set; } = default!;
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int TotalRows { get; set; }
    public int InsertedRows { get; set; }
    public int FailedRows { get; set; }
    public ImportStatus Status { get; set; }
    /// <summary>Short outcome sentence shown in history.</summary>
    public string? Message { get; set; }
    /// <summary>The rows that were not inserted, with their values and reasons, as JSON. Source of the downloadable error report.</summary>
    public string? ErrorReportJson { get; set; }
}
