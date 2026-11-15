namespace ChildGuard.Application.Import;

/// <summary>One physical spreadsheet row. <see cref="RowNumber"/> is 1-based, as the admin sees it in Excel.</summary>
public sealed record SpreadsheetRow(int RowNumber, IReadOnlyList<string?> Cells, IReadOnlySet<int> FormulaColumns)
{
    public bool IsBlank => Cells.All(string.IsNullOrWhiteSpace) && FormulaColumns.Count == 0;
}

/// <summary>A file that cannot be imported at all (wrong type, too big, corrupt). The message is safe to show the admin.</summary>
public sealed class ImportFileException : Exception
{
    public ImportFileException(string message) : base(message) { }
}

/// <summary>Hard limits applied while reading an uploaded file, before any row is looked at.</summary>
public static class ImportLimits
{
    /// <summary>Largest accepted upload.</summary>
    public const long MaxFileBytes = 5L * 1024 * 1024;
    /// <summary>An .xlsx is a zip; this caps what it may expand to, so a small "zip bomb" cannot exhaust memory.</summary>
    public const long MaxXlsxUncompressedBytes = 60L * 1024 * 1024;
    public const int MaxXlsxEntries = 300;
    public const int MaxColumns = 40;
    public const int MaxCellLength = 1000;

    /// <summary>Hard cap on rows read from any file, above every category's own limit.</summary>
    public const int MaxRowsRead = ImportSchema.DefaultMaxRows;
}

/// <summary>
/// Spreadsheet formula injection ("CSV injection"). A value beginning with one of these
/// characters can be executed as a formula when a CSV is opened in a spreadsheet program.
/// </summary>
public static class FormulaInjection
{
    private static readonly char[] Triggers = { '=', '+', '-', '@', '\t', '\r', '\n' };

    public static bool StartsWithTrigger(string? value) =>
        !string.IsNullOrEmpty(value) && Array.IndexOf(Triggers, value[0]) >= 0;

    /// <summary>Neutralises a value for CSV output: a leading apostrophe makes spreadsheets treat it as text.</summary>
    public static string Escape(string? value) =>
        value is null ? string.Empty : StartsWithTrigger(value) ? "'" + value : value;
}
