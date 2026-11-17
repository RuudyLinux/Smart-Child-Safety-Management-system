using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Import;

public sealed record ImportFileContent(ImportSource Source, List<ImportRowDto> Rows, List<string> Warnings);

/// <summary>
/// Turns an uploaded .csv or .xlsx into rows keyed by column. The file type is decided by
/// extension and confirmed from the bytes, so a renamed file is refused. Header matching ignores
/// case, spaces and punctuation ("Date of Birth *" matches "dateOfBirth").
/// </summary>
public static class ImportFileReader
{
    /// <summary>
    /// Value given to a cell that held a formula. Validation rejects it with an explanation, and
    /// because it is a value (not a flag) it is rejected again if it comes back on commit.
    /// </summary>
    public const string FormulaSentinel = "=FORMULA()";

    public static readonly string[] AcceptedExtensions = { ".xlsx", ".csv" };

    public static ImportFileContent Read(ImportCategoryDefinition definition, byte[] bytes, string? fileName)
    {
        if (bytes.Length == 0) throw new ImportFileException("The file is empty.");
        if (bytes.Length > ImportLimits.MaxFileBytes)
            throw new ImportFileException($"The file is larger than {ImportLimits.MaxFileBytes / (1024 * 1024)} MB. Split it into smaller files.");

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        List<SpreadsheetRow> sheet;
        ImportSource source;
        switch (extension)
        {
            case ".xlsx":
                source = ImportSource.Excel;
                sheet = XlsxFormat.Read(bytes, definition.MaxRows);
                break;
            case ".csv":
                if (XlsxFormat.LooksLikeZip(bytes))
                    throw new ImportFileException("This file is an Excel workbook, not a CSV. Upload it with the .xlsx extension.");
                source = ImportSource.Csv;
                sheet = CsvFormat.Read(bytes, definition.MaxRows);
                break;
            default:
                throw new ImportFileException("Only Excel (.xlsx) and CSV (.csv) files are accepted.");
        }

        if (sheet.Count == 0 || sheet[0].IsBlank)
            throw new ImportFileException("The file has no header row. Start from the downloaded template.");

        var warnings = new List<string>();
        var map = MapHeaders(definition, sheet[0], warnings);

        var rows = new List<ImportRowDto>(sheet.Count - 1);
        foreach (var raw in sheet.Skip(1))
        {
            if (raw.IsBlank) continue;
            var values = definition.Columns.ToDictionary(c => c.Key, _ => (string?)null);
            foreach (var (index, key) in map)
            {
                if (raw.FormulaColumns.Contains(index)) values[key] = FormulaSentinel;
                else if (index < raw.Cells.Count) values[key] = raw.Cells[index];
            }
            rows.Add(new ImportRowDto(raw.RowNumber, values));
        }

        if (rows.Count == 0)
            throw new ImportFileException("The file has a header row but no data rows.");
        if (rows.Count > definition.MaxRows)
            throw new ImportFileException($"{definition.Name} imports are limited to {definition.MaxRows} rows; the file has {rows.Count}. Split it into smaller files.");

        return new ImportFileContent(source, rows, warnings);
    }

    private static Dictionary<int, string> MapHeaders(ImportCategoryDefinition definition, SpreadsheetRow header, List<string> warnings)
    {
        var lookup = new Dictionary<string, string>();
        foreach (var column in definition.Columns)
        {
            lookup[Normalize(column.Header)] = column.Key;
            lookup[Normalize(column.Key)] = column.Key;
        }

        var map = new Dictionary<int, string>();
        for (var i = 0; i < header.Cells.Count; i++)
        {
            var text = header.Cells[i];
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!lookup.TryGetValue(Normalize(text), out var key))
            {
                warnings.Add($"Column \"{Clip(text)}\" is not part of the {definition.Name} template and was ignored.");
                continue;
            }
            if (map.ContainsValue(key))
                throw new ImportFileException($"The column \"{definition.Column(key)!.Header}\" appears more than once.");
            map[i] = key;
        }

        var missing = definition.Columns.Where(c => c.Required && !map.ContainsValue(c.Key)).Select(c => c.Header).ToList();
        if (missing.Count > 0)
            throw new ImportFileException($"Required column(s) missing: {string.Join(", ", missing)}. Start from the downloaded {definition.Name} template.");

        return map;
    }

    private static string Normalize(string header) =>
        new string(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Clip(string value) => value.Length > 40 ? value[..40] + "…" : value;
}
