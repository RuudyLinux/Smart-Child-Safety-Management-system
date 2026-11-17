using System.IO.Compression;
using System.Text;
using ChildGuard.Application.Import;

namespace ChildGuard.Application.Tests;

/// <summary>
/// The file layer of bulk import: CSV and XLSX are read faithfully, and hostile files
/// (formulas, DTDs, zip bombs, renamed types) are refused or neutralised.
/// </summary>
public class BulkImportFileTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    // ---------------------------------------------------------------- CSV

    [Fact]
    public void Csv_ReadsQuotedFieldsWithDelimitersQuotesAndLineBreaks()
    {
        var rows = CsvFormat.Read(Utf8("Full Name,Grade\r\n\"Sharma, Aarav\",\"Grade \"\"3\"\"\"\n\"Two\nLines\",4\n"));

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Sharma, Aarav", "Grade \"3\"" }, rows[1].Cells);
        Assert.Equal("Two\nLines", rows[2].Cells[0]);
        Assert.Equal(3, rows[2].RowNumber);
    }

    [Fact]
    public void Csv_HandlesBomSemicolonsAndSkipsBlankLines()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Utf8("Route Name;Description\n\nNorth;Via market\n;\n")).ToArray();

        var rows = CsvFormat.Read(bytes);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Route Name", rows[0].Cells[0]);
        Assert.Equal(new[] { "North", "Via market" }, rows[1].Cells);
    }

    [Fact]
    public void Csv_RefusesNonUtf8AndBinary()
    {
        var latin1 = new byte[] { (byte)'N', (byte)'a', 0xE9, (byte)'\n' };
        Assert.Contains("UTF-8", Assert.Throws<ImportFileException>(() => CsvFormat.Read(latin1)).Message);
        Assert.Throws<ImportFileException>(() => CsvFormat.Read(new byte[] { 65, 0, 66 }));
    }

    [Fact]
    public void Csv_RefusesUnterminatedQuoteAndTooManyRows()
    {
        Assert.Throws<ImportFileException>(() => CsvFormat.Read(Utf8("a\n\"open")));
        var many = "h\n" + string.Join("\n", Enumerable.Range(0, 11).Select(i => "v" + i));
        Assert.Contains("more than 10", Assert.Throws<ImportFileException>(() => CsvFormat.Read(Utf8(many), maxRows: 10)).Message);
    }

    [Fact]
    public void Csv_WriterNeutralisesFormulas()
    {
        var text = Encoding.UTF8.GetString(CsvFormat.Write(new[] { new string?[] { "=HYPERLINK(\"x\")", "+91 98", "-1", "@SUM", "plain" } }));

        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", text);
        Assert.Contains("\"'+91 98\"", text);
        Assert.Contains("\"'-1\"", text);
        Assert.Contains("\"'@SUM\"", text);
        Assert.Contains("\"plain\"", text);
    }

    // ---------------------------------------------------------------- XLSX

    [Fact]
    public void Xlsx_WrittenWorkbookReadsBack()
    {
        var bytes = XlsxFormat.Write(new[]
        {
            new XlsxSheet("Guardians", new[]
            {
                new string?[] { "Full Name", "Email", "Contact Number" },
                new string?[] { "Priya Sharma", "priya@example.com", "+91 98200 11111" },
                new string?[] { "Ümit <&> \"Q\"", null, "0098" },
            }),
            new XlsxSheet("Instructions", new[] { new string?[] { "ignored" } })
        });

        var rows = XlsxFormat.Read(bytes);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Priya Sharma", "priya@example.com", "+91 98200 11111" }, rows[1].Cells);
        Assert.Equal("Ümit <&> \"Q\"", rows[2].Cells[0]);
        Assert.Null(rows[2].Cells[1]);
        Assert.Equal("0098", rows[2].Cells[2]);
    }

    [Fact]
    public void Xlsx_ReadsSharedStringsNumbersAndBooleans()
    {
        var bytes = Workbook(
            sheet: """
                   <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
                     <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c></row>
                     <row r="3"><c r="A3" t="s"><v>3</v></c><c r="B3"><v>9876543210</v></c><c r="D3" t="b"><v>1</v></c></row>
                   </sheetData></worksheet>
                   """,
            sharedStrings: """
                           <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                             <si><t>Name</t></si><si><t>Phone</t></si><si><t>X</t></si>
                             <si><r><t>Ra</t></r><r><t>mesh</t></r><rPh><t>phonetic</t></rPh></si>
                           </sst>
                           """);

        var rows = XlsxFormat.Read(bytes);

        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows[1].RowNumber);
        Assert.Equal("Ramesh", rows[1].Cells[0]);
        Assert.Equal("9876543210", rows[1].Cells[1]);
        Assert.Null(rows[1].Cells[2]);
        Assert.Equal("TRUE", rows[1].Cells[3]);
    }

    [Fact]
    public void Xlsx_FormulaCellsAreFlaggedAndTheirCachedResultDropped()
    {
        var bytes = Workbook("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
              <row r="1"><c r="A1" t="inlineStr"><is><t>Full Name</t></is></c></row>
              <row r="2"><c r="A2" t="str"><f>HYPERLINK("http://evil","Click")</f><v>Click</v></c></row>
            </sheetData></worksheet>
            """);

        var row = XlsxFormat.Read(bytes)[1];

        Assert.Contains(0, row.FormulaColumns);
        Assert.Null(row.Cells[0]);
    }

    [Fact]
    public void Xlsx_RefusesDtdsSoEntityAttacksCannotRun()
    {
        var bytes = Workbook("""
            <?xml version="1.0"?>
            <!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;">]>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
              <row r="1"><c r="A1" t="inlineStr"><is><t>&lol2;</t></is></c></row>
            </sheetData></worksheet>
            """);

        Assert.Contains("unsupported XML", Assert.Throws<ImportFileException>(() => XlsxFormat.Read(bytes)).Message);
    }

    [Fact]
    public void Xlsx_RefusesAZipBombWithoutExpandingIt()
    {
        // ~61 MB of spaces compresses to well under 1 MB.
        var padding = new string(' ', (int)ImportLimits.MaxXlsxUncompressedBytes + 1024 * 1024);
        var bytes = Workbook("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" + padding + "<sheetData/></worksheet>");
        Assert.True(bytes.Length < ImportLimits.MaxFileBytes);

        Assert.Contains("expands", Assert.Throws<ImportFileException>(() => XlsxFormat.Read(bytes)).Message);
    }

    [Fact]
    public void Xlsx_RefusesOldXlsAndDamagedFiles()
    {
        var ole = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        Assert.Contains(".xls", Assert.Throws<ImportFileException>(() => XlsxFormat.Read(ole)).Message);
        Assert.Throws<ImportFileException>(() => XlsxFormat.Read(new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 }));
    }

    [Theory]
    [InlineData("A1", 0)]
    [InlineData("Z9", 25)]
    [InlineData("AA10", 26)]
    [InlineData("b2", 1)]
    public void Xlsx_ColumnReferences(string reference, int index) => Assert.Equal(index, XlsxFormat.ColumnIndex(reference));

    // ---------------------------------------------------------------- file reader

    [Fact]
    public void FileReader_MapsHeadersLooselyAndWarnsAboutUnknownColumns()
    {
        var csv = Utf8("FULL NAME *,e-mail,Contact number,Notes\nPriya,p@example.com,9820011111,hi\n");

        var content = ImportFileReader.Read(ImportSchema.Guardians, csv, "guardians.csv");

        var row = Assert.Single(content.Rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("Priya", row.Values[ImportSchema.Keys.FullName]);
        Assert.Equal("p@example.com", row.Values[ImportSchema.Keys.Email]);
        Assert.Contains(content.Warnings, w => w.Contains("Notes"));
    }

    [Fact]
    public void FileReader_RefusesMissingRequiredColumnsAndWrongTypes()
    {
        Assert.Contains("Email", Assert.Throws<ImportFileException>(() =>
            ImportFileReader.Read(ImportSchema.Guardians, Utf8("Full Name,Contact Number\nA,1\n"), "g.csv")).Message);
        Assert.Throws<ImportFileException>(() => ImportFileReader.Read(ImportSchema.Guardians, Utf8("x"), "g.txt"));
        var xlsx = XlsxFormat.Write(new[] { new XlsxSheet("S", new[] { new string?[] { "Full Name" } }) });
        Assert.Contains("Excel workbook", Assert.Throws<ImportFileException>(() => ImportFileReader.Read(ImportSchema.Routes, xlsx, "renamed.csv")).Message);
        Assert.Contains("no data", Assert.Throws<ImportFileException>(() => ImportFileReader.Read(ImportSchema.Routes, Utf8("Route Name\n"), "r.csv")).Message);
        Assert.Throws<ImportFileException>(() => ImportFileReader.Read(ImportSchema.Routes, new byte[ImportLimits.MaxFileBytes + 1], "r.csv"));
    }

    [Fact]
    public void FileReader_EnforcesTheCategoryRowLimit()
    {
        var csv = "Full Name,Email,Contact Number\n" + string.Join("\n",
            Enumerable.Range(0, ImportSchema.GuardianMaxRows + 1).Select(i => $"G{i},g{i}@example.com,98200{i:00000}"));

        Assert.Throws<ImportFileException>(() => ImportFileReader.Read(ImportSchema.Guardians, Utf8(csv), "g.csv"));
    }

    [Fact]
    public void FileReader_TurnsFormulaCellsIntoAValueValidationRejects()
    {
        var bytes = Workbook("""
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
              <row r="1"><c r="A1" t="inlineStr"><is><t>Route Name</t></is></c></row>
              <row r="2"><c r="A2"><f>1+1</f><v>2</v></c></row>
            </sheetData></worksheet>
            """);

        var row = Assert.Single(ImportFileReader.Read(ImportSchema.Routes, bytes, "r.xlsx").Rows);

        Assert.Equal(ImportFileReader.FormulaSentinel, row.Values[ImportSchema.Keys.RouteName]);
        var plan = new ImportRowReader(ImportSchema.Routes, row).Plan;
        Assert.Contains(plan.Errors, e => e.Message.Contains("formula"));
    }

    /// <summary>A minimal workbook around raw worksheet XML.</summary>
    private static byte[] Workbook(string sheet, string? sharedStrings = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var s = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
                var b = Encoding.UTF8.GetBytes(content);
                s.Write(b, 0, b.Length);
            }
            Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Add("xl/worksheets/sheet1.xml", sheet.TrimStart());
            if (sharedStrings is not null) Add("xl/sharedStrings.xml", sharedStrings);
        }
        return ms.ToArray();
    }
}
