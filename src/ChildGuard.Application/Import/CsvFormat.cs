using System.Text;

namespace ChildGuard.Application.Import;

/// <summary>
/// RFC 4180 CSV reading and writing. Reads UTF-8 only (with or without BOM); comma or semicolon
/// delimited (Excel uses ';' in many locales), quoted fields may contain delimiters and line breaks.
/// </summary>
public static class CsvFormat
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static List<SpreadsheetRow> Read(byte[] bytes, int maxRows = ImportLimits.MaxRowsRead)
    {
        if (Array.IndexOf(bytes, (byte)0) >= 0)
            throw new ImportFileException("This file is not a text CSV file. Save it from Excel as \"CSV UTF-8 (Comma delimited)\".");

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ImportFileException("The CSV file is not UTF-8 encoded. In Excel choose Save As › \"CSV UTF-8 (Comma delimited)\".");
        }
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var delimiter = DetectDelimiter(text);
        var rows = new List<SpreadsheetRow>();
        var noFormulas = new HashSet<int>();
        var cells = new List<string?>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;
        var rowNumber = 1;

        void EndField()
        {
            if (field.Length > ImportLimits.MaxCellLength)
                throw new ImportFileException($"Row {rowNumber} has a value longer than {ImportLimits.MaxCellLength} characters.");
            cells.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
            if (cells.Count > ImportLimits.MaxColumns)
                throw new ImportFileException($"Row {rowNumber} has more than {ImportLimits.MaxColumns} columns.");
        }

        void EndRow()
        {
            EndField();
            var row = new SpreadsheetRow(rowNumber, cells.ToArray(), noFormulas);
            if (!row.IsBlank || rowNumber == 1)
            {
                // +1 for the header row.
                if (rows.Count >= maxRows + 1)
                    throw new ImportFileException($"The file has more than {maxRows} data rows. Split it into smaller files.");
                rows.Add(row);
            }
            cells.Clear();
            rowNumber++;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            if (c == '"' && !fieldStarted && field.Length == 0) { inQuotes = true; fieldStarted = true; }
            else if (c == delimiter) EndField();
            // "\r\n" ends a row at its "\n"; a lone "\r" (old Mac line end) ends it on its own.
            else if (c == '\r') { if (i + 1 >= text.Length || text[i + 1] != '\n') EndRow(); }
            else if (c == '\n') EndRow();
            else { field.Append(c); fieldStarted = true; }
        }

        if (inQuotes)
            throw new ImportFileException($"Row {rowNumber} has an opening quote that is never closed.");
        if (field.Length > 0 || cells.Count > 0) EndRow();

        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        int commas = 0, semicolons = 0;
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes && (c == '\n' || c == '\r')) break;
            else if (!inQuotes && c == ',') commas++;
            else if (!inQuotes && c == ';') semicolons++;
        }
        return semicolons > commas ? ';' : ',';
    }

    /// <summary>
    /// Writes rows as UTF-8 CSV with a BOM (so Excel detects the encoding). Every value is
    /// quoted and formula-escaped: a downloaded report can never execute anything.
    /// </summary>
    public static byte[] Write(IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendJoin(',', row.Select(v => "\"" + FormulaInjection.Escape(v).Replace("\"", "\"\"") + "\""));
            sb.Append("\r\n");
        }
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return preamble.Concat(body).ToArray();
    }
}
