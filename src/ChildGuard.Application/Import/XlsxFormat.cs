using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace ChildGuard.Application.Import;

/// <summary>One worksheet to write. The first row is styled as a header and frozen.</summary>
public sealed record XlsxSheet(string Name, IReadOnlyList<IReadOnlyList<string?>> Rows, IReadOnlyList<double>? ColumnWidths = null);

/// <summary>
/// Minimal, defensive Office Open XML spreadsheet support, built on the BCL only.
/// Reading takes the first worksheet's cell values. It never evaluates anything: formula cells
/// are reported, not computed, so a workbook cannot smuggle a formula's cached result in.
/// The package is treated as hostile: the entry count and the total expanded size are capped while
/// decompressing (not taken from the zip headers, which can lie), and XML DTDs are refused, which
/// rules out entity-expansion and external-entity attacks.
/// </summary>
public static class XlsxFormat
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static bool LooksLikeZip(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;

    // ------------------------------------------------------------------ reading

    public static List<SpreadsheetRow> Read(byte[] bytes, int maxRows = ImportLimits.MaxRowsRead)
    {
        if (!LooksLikeZip(bytes))
            throw new ImportFileException("This is not a valid .xlsx workbook. Old .xls files and password-protected workbooks are not supported: save it as an Excel Workbook (.xlsx) or CSV UTF-8.");

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new ImportFileException("The .xlsx file is damaged and cannot be opened.");
        }

        using (zip)
        {
            if (zip.Entries.Count > ImportLimits.MaxXlsxEntries)
                throw new ImportFileException("The .xlsx file has an unexpected structure and was refused.");

            var budget = new ExpansionBudget(ImportLimits.MaxXlsxUncompressedBytes);
            try
            {
                var sheetPath = ResolveFirstSheetPath(zip, budget)
                    ?? throw new ImportFileException("The workbook has no worksheet.");
                var shared = ReadSharedStrings(zip, budget);
                return ReadSheet(zip, sheetPath, shared, maxRows, budget);
            }
            catch (XmlException)
            {
                throw new ImportFileException("The .xlsx file contains invalid or unsupported XML and was refused.");
            }
            catch (InvalidDataException)
            {
                throw new ImportFileException("The .xlsx file is damaged and cannot be opened.");
            }
        }
    }

    private static XmlReader OpenXml(ZipArchiveEntry entry, ExpansionBudget budget)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersFromEntities = 0,
            CloseInput = true
        };
        return XmlReader.Create(new BudgetedStream(entry.Open(), budget), settings);
    }

    private static ZipArchiveEntry? Entry(ZipArchive zip, string path) =>
        zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));

    private static string? ResolveFirstSheetPath(ZipArchive zip, ExpansionBudget budget)
    {
        var workbook = Entry(zip, "xl/workbook.xml");
        string? relId = null;
        if (workbook is not null)
        {
            using var r = OpenXml(workbook, budget);
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element || r.LocalName != "sheet") continue;
                for (var i = 0; i < r.AttributeCount; i++)
                {
                    r.MoveToAttribute(i);
                    if (r.LocalName == "id" && r.NamespaceURI.Contains("relationships", StringComparison.Ordinal)) relId = r.Value;
                }
                break;
            }
        }

        var rels = Entry(zip, "xl/_rels/workbook.xml.rels");
        if (relId is not null && rels is not null)
        {
            using var r = OpenXml(rels, budget);
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element || r.LocalName != "Relationship" || r.GetAttribute("Id") != relId) continue;
                var target = r.GetAttribute("Target");
                if (string.IsNullOrWhiteSpace(target)) break;
                var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                if (Entry(zip, path) is not null) return path;
                break;
            }
        }

        return Entry(zip, "xl/worksheets/sheet1.xml") is not null ? "xl/worksheets/sheet1.xml" : null;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip, ExpansionBudget budget)
    {
        var list = new List<string>();
        var entry = Entry(zip, "xl/sharedStrings.xml");
        if (entry is null) return list;

        using var r = OpenXml(entry, budget);
        StringBuilder? current = null;
        var phoneticDepth = 0;
        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                switch (r.LocalName)
                {
                    case "si":
                        current = new StringBuilder();
                        if (r.IsEmptyElement) { list.Add(string.Empty); current = null; }
                        break;
                    case "rPh":
                        if (!r.IsEmptyElement) phoneticDepth++;
                        break;
                    case "t" when current is not null && phoneticDepth == 0:
                        current.Append(r.ReadElementContentAsString());
                        continue; // already positioned on the next node
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement)
            {
                if (r.LocalName == "rPh" && phoneticDepth > 0) phoneticDepth--;
                else if (r.LocalName == "si" && current is not null) { list.Add(current.ToString()); current = null; }
            }
            r.Read();
        }
        return list;
    }

    private static List<SpreadsheetRow> ReadSheet(ZipArchive zip, string path, List<string> shared, int maxRows, ExpansionBudget budget)
    {
        var rows = new List<SpreadsheetRow>();
        using var r = OpenXml(Entry(zip, path)!, budget);

        var rowNumber = 0;
        Dictionary<int, string?>? cells = null;
        HashSet<int>? formulas = null;

        var col = -1;
        string? type = null;
        string? value = null;
        StringBuilder? inline = null;
        var hasFormula = false;

        void FinishRow()
        {
            if (cells is null) return;
            var width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            var dense = new string?[width];
            foreach (var (k, v) in cells) dense[k] = v;
            var row = new SpreadsheetRow(rowNumber, dense, formulas!);
            if (!row.IsBlank || rows.Count == 0)
            {
                if (rows.Count >= maxRows + 1)
                    throw new ImportFileException($"The file has more than {maxRows} data rows. Split it into smaller files.");
                rows.Add(row);
            }
            cells = null;
        }

        while (!r.EOF)
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                switch (r.LocalName)
                {
                    case "row":
                        FinishRow();
                        rowNumber = int.TryParse(r.GetAttribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var rn) && rn > 0 ? rn : rowNumber + 1;
                        cells = new Dictionary<int, string?>();
                        formulas = new HashSet<int>();
                        col = -1;
                        if (r.IsEmptyElement) FinishRow();
                        break;

                    case "c" when cells is not null:
                        col = ColumnIndex(r.GetAttribute("r")) ?? col + 1;
                        type = r.GetAttribute("t");
                        value = null;
                        inline = null;
                        hasFormula = false;
                        break;

                    case "f" when cells is not null:
                        hasFormula = true;
                        break;

                    case "v" when cells is not null:
                        value = r.ReadElementContentAsString();
                        continue;

                    case "is" when cells is not null:
                        inline = new StringBuilder();
                        break;

                    case "t" when inline is not null:
                        inline.Append(r.ReadElementContentAsString());
                        continue;
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement)
            {
                if (r.LocalName == "c" && cells is not null)
                {
                    StoreCell(cells, formulas!, rowNumber, col, type, value, inline?.ToString(), hasFormula, shared);
                    inline = null;
                }
                else if (r.LocalName == "is") { /* keep inline until the cell ends */ }
                else if (r.LocalName == "row") FinishRow();
            }
            r.Read();
        }
        FinishRow();
        return rows;
    }

    private static void StoreCell(Dictionary<int, string?> cells, HashSet<int> formulas, int rowNumber, int col,
        string? type, string? value, string? inline, bool hasFormula, List<string> shared)
    {
        if (col < 0) return;
        var isError = type == "e";
        string? text = type switch
        {
            "s" => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var idx) && idx >= 0 && idx < shared.Count ? shared[idx] : null,
            "inlineStr" => inline,
            "b" => value == "1" ? "TRUE" : value == "0" ? "FALSE" : value,
            "str" or "d" => value,
            "e" => null,
            _ => NormalizeNumber(value)
        };

        if (col >= ImportLimits.MaxColumns)
        {
            if (!string.IsNullOrWhiteSpace(text) || hasFormula)
                throw new ImportFileException($"Row {rowNumber} has more than {ImportLimits.MaxColumns} columns.");
            return;
        }
        if (text is { Length: > ImportLimits.MaxCellLength })
            throw new ImportFileException($"Row {rowNumber} has a value longer than {ImportLimits.MaxCellLength} characters.");

        if (hasFormula || isError)
        {
            // The formula's cached result is deliberately discarded.
            formulas.Add(col);
            cells[col] = null;
            return;
        }
        if (text is not null) cells[col] = text;
    }

    /// <summary>Numbers come back as Excel stored them ("9876543210", "1.5E+3"); render them plainly.</summary>
    private static string? NormalizeNumber(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d.ToString("0.############", CultureInfo.InvariantCulture)
            : raw;
    }

    /// <summary>"B12" → 1. Null when the reference is missing or malformed.</summary>
    public static int? ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return null;
        var index = 0;
        var letters = 0;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z') { index = index * 26 + (ch - 'A' + 1); letters++; }
            else if (ch is >= 'a' and <= 'z') { index = index * 26 + (ch - 'a' + 1); letters++; }
            else break;
            if (letters > 3) return null;
        }
        return letters == 0 ? null : index - 1;
    }

    public static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
            name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    private sealed class ExpansionBudget
    {
        public ExpansionBudget(long remaining) => Remaining = remaining;
        public long Remaining { get; set; }
    }

    /// <summary>Counts decompressed bytes against the shared budget and stops the read once it is spent.</summary>
    private sealed class BudgetedStream : Stream
    {
        private readonly Stream _inner;
        private readonly ExpansionBudget _budget;

        public BudgetedStream(Stream inner, ExpansionBudget budget)
        {
            _inner = inner;
            _budget = budget;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _inner.Read(buffer, offset, count);
            _budget.Remaining -= n;
            if (_budget.Remaining < 0)
                throw new ImportFileException("The .xlsx file expands to more data than allowed and was refused.");
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ writing

    /// <summary>
    /// Writes a workbook. Every cell is an inline string (never a formula) and every column is
    /// formatted as Text, so numbers typed into the template later (phone numbers, card UIDs)
    /// keep their leading '+' and zeros.
    /// </summary>
    public static byte[] Write(IReadOnlyList<XlsxSheet> sheets)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", w =>
            {
                w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                Default(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
                Default(w, "xml", "application/xml");
                Override(w, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                Override(w, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                for (var i = 0; i < sheets.Count; i++)
                    Override(w, $"/xl/worksheets/sheet{i + 1}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                w.WriteEndElement();
            });

            WriteEntry(zip, "_rels/.rels", w =>
            {
                w.WriteStartElement("Relationships", PackageRelNs);
                Relationship(w, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
                w.WriteEndElement();
            });

            WriteEntry(zip, "xl/workbook.xml", w =>
            {
                w.WriteStartElement("workbook", MainNs);
                w.WriteAttributeString("xmlns", "r", null, RelNs);
                w.WriteStartElement("sheets", MainNs);
                for (var i = 0; i < sheets.Count; i++)
                {
                    w.WriteStartElement("sheet", MainNs);
                    w.WriteAttributeString("name", SheetName(sheets[i].Name, i));
                    w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
                    w.WriteAttributeString("id", RelNs, $"rId{i + 1}");
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                w.WriteEndElement();
            });

            WriteEntry(zip, "xl/_rels/workbook.xml.rels", w =>
            {
                w.WriteStartElement("Relationships", PackageRelNs);
                for (var i = 0; i < sheets.Count; i++)
                    Relationship(w, $"rId{i + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", $"worksheets/sheet{i + 1}.xml");
                Relationship(w, $"rId{sheets.Count + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
                w.WriteEndElement();
            });

            {
                var styles = zip.CreateEntry("xl/styles.xml", CompressionLevel.Optimal);
                using var s = styles.Open();
                var xml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + StylesXml);
                s.Write(xml, 0, xml.Length);
            }

            for (var i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                WriteEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", w => WriteSheet(w, sheet));
            }
        }
        return ms.ToArray();
    }

    // Style 0 default, 1 text-formatted, 2 text-formatted bold header on a light fill.
    private const string StylesXml =
        "<styleSheet xmlns=\"" + MainNs + "\">" +
        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEAF0F7\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"3\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"49\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\"/>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    private static void WriteSheet(XmlWriter w, XlsxSheet sheet)
    {
        var columnCount = Math.Max(sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(r => r.Count), sheet.ColumnWidths?.Count ?? 0);

        w.WriteStartElement("worksheet", MainNs);
        w.WriteAttributeString("xmlns", "r", null, RelNs);

        w.WriteStartElement("sheetViews", MainNs);
        w.WriteStartElement("sheetView", MainNs);
        w.WriteAttributeString("workbookViewId", "0");
        w.WriteStartElement("pane", MainNs);
        w.WriteAttributeString("ySplit", "1");
        w.WriteAttributeString("topLeftCell", "A2");
        w.WriteAttributeString("activePane", "bottomLeft");
        w.WriteAttributeString("state", "frozen");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        if (columnCount > 0)
        {
            w.WriteStartElement("cols", MainNs);
            for (var c = 0; c < columnCount; c++)
            {
                var width = sheet.ColumnWidths is { } widths && c < widths.Count ? widths[c] : 22;
                w.WriteStartElement("col", MainNs);
                w.WriteAttributeString("min", (c + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("max", (c + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("width", width.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("style", "1");
                w.WriteAttributeString("customWidth", "1");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        w.WriteStartElement("sheetData", MainNs);
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            w.WriteStartElement("row", MainNs);
            w.WriteAttributeString("r", (r + 1).ToString(CultureInfo.InvariantCulture));
            for (var c = 0; c < row.Count; c++)
            {
                var value = XmlSafe(row[c]);
                if (string.IsNullOrEmpty(value)) continue;
                w.WriteStartElement("c", MainNs);
                w.WriteAttributeString("r", ColumnName(c) + (r + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("t", "inlineStr");
                w.WriteAttributeString("s", r == 0 ? "2" : "1");
                w.WriteStartElement("is", MainNs);
                w.WriteStartElement("t", MainNs);
                w.WriteAttributeString("xml", "space", null, "preserve");
                w.WriteString(value);
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static string? XmlSafe(string? value) =>
        value is null || value.All(XmlConvert.IsXmlChar) ? value : new string(value.Where(XmlConvert.IsXmlChar).ToArray());

    private static string SheetName(string name, int index)
    {
        var clean = new string(name.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
        if (clean.Length == 0) clean = $"Sheet{index + 1}";
        return clean.Length > 31 ? clean[..31] : clean;
    }

    private static void WriteEntry(ZipArchive zip, string path, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var w = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
        w.WriteStartDocument(true);
        write(w);
        w.WriteEndDocument();
    }

    private static void Default(XmlWriter w, string ext, string type)
    {
        w.WriteStartElement("Default", "http://schemas.openxmlformats.org/package/2006/content-types");
        w.WriteAttributeString("Extension", ext);
        w.WriteAttributeString("ContentType", type);
        w.WriteEndElement();
    }

    private static void Override(XmlWriter w, string part, string type)
    {
        w.WriteStartElement("Override", "http://schemas.openxmlformats.org/package/2006/content-types");
        w.WriteAttributeString("PartName", part);
        w.WriteAttributeString("ContentType", type);
        w.WriteEndElement();
    }

    private static void Relationship(XmlWriter w, string id, string type, string target)
    {
        w.WriteStartElement("Relationship", PackageRelNs);
        w.WriteAttributeString("Id", id);
        w.WriteAttributeString("Type", type);
        w.WriteAttributeString("Target", target);
        w.WriteEndElement();
    }
}
