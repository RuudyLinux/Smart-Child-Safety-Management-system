using System.Globalization;
using System.Net.Mail;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using FluentValidation.Results;

namespace ChildGuard.Application.Import;

/// <summary>
/// The validation state of one row while it is being planned. Values are trimmed; blank becomes null.
/// </summary>
public sealed class RowPlan
{
    public RowPlan(int rowNumber, Dictionary<string, string?> values)
    {
        RowNumber = rowNumber;
        Values = values;
    }

    public int RowNumber { get; }
    public Dictionary<string, string?> Values { get; }
    public List<ImportFieldErrorDto> Errors { get; } = new();
    /// <summary>The typed record built from a valid row, staged for insert on commit.</summary>
    public object? Parsed { get; set; }
    public bool IsValid => Errors.Count == 0;

    public bool HasError(string key) => Errors.Any(e => e.Column == key);

    public void Error(string? key, string message)
    {
        if (!Errors.Any(e => e.Column == key && e.Message == message))
            Errors.Add(new ImportFieldErrorDto(key, message));
    }

    public ImportRowResultDto ToResult() => new(RowNumber, Values, IsValid, Errors.ToList());
}

/// <summary>
/// Reads and checks the cells of one row: safety (formula injection, control characters),
/// length, and the type of each column. Every problem is recorded against its column; nothing throws.
/// </summary>
public sealed class ImportRowReader
{
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd",
        "dd-MM-yyyy", "dd/MM/yyyy", "dd.MM.yyyy", "d-M-yyyy", "d/M/yyyy", "d.M.yyyy",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"
    };

    private readonly ImportCategoryDefinition _definition;

    public ImportRowReader(ImportCategoryDefinition definition, ImportRowDto row)
    {
        _definition = definition;
        var values = new Dictionary<string, string?>();
        foreach (var column in definition.Columns)
        {
            row.Values.TryGetValue(column.Key, out var raw);
            values[column.Key] = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }
        Plan = new RowPlan(row.RowNumber, values);

        foreach (var column in definition.Columns) CheckCell(column);
        if (definition.Columns.All(c => Plan.Values[c.Key] is null))
            Plan.Error(null, "The row is empty.");
    }

    public RowPlan Plan { get; }

    private void CheckCell(ImportColumn column)
    {
        var value = Plan.Values[column.Key];
        if (value is null)
        {
            if (column.Required) Plan.Error(column.Key, $"{column.Header} is required.");
            return;
        }

        if (value == ImportFileReader.FormulaSentinel)
        {
            Plan.Error(column.Key, "This cell contains a formula. Type the value itself.");
            return;
        }
        if (value.Any(char.IsControl))
        {
            Plan.Error(column.Key, $"{column.Header} contains line breaks or control characters.");
            return;
        }
        // '=' and '@' never begin a legitimate value. '+' and '-' do for numbers and phone numbers
        // ("+91 …"), which are then checked as such; anywhere else they could start a formula.
        var leadingSignAllowed = column.Kind is ImportColumnKind.Phone or ImportColumnKind.Integer;
        if (value[0] is '=' or '@' || (!leadingSignAllowed && value[0] is '+' or '-'))
        {
            Plan.Error(column.Key, $"{column.Header} may not start with '{value[0]}' (spreadsheet formula protection).");
            return;
        }
        if (column.MaxLength is int max && value.Length > max)
        {
            Plan.Error(column.Key, $"{column.Header} must be {max} characters or fewer.");
            return;
        }

        switch (column.Kind)
        {
            case ImportColumnKind.Email when !IsEmail(value):
                Plan.Error(column.Key, $"{column.Header} is not a valid email address.");
                break;
            case ImportColumnKind.Phone when ContactNumber.Normalize(value) is not { } digits || digits.TrimStart('+').Length is < 8 or > 15
                                            || value.Any(c => !(char.IsDigit(c) || c is ' ' or '-' or '(' or ')' or '+' or '.')):
                Plan.Error(column.Key, $"{column.Header} must be a phone number with 8 to 15 digits.");
                break;
            case ImportColumnKind.Date when ParseDate(value) is null:
                Plan.Error(column.Key, $"{column.Header} is not a valid date. Use YYYY-MM-DD.");
                break;
            case ImportColumnKind.Integer when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _):
                Plan.Error(column.Key, $"{column.Header} must be a whole number.");
                break;
            case ImportColumnKind.Boolean when ParseBool(value) is null:
                Plan.Error(column.Key, $"{column.Header} must be Yes or No.");
                break;
        }
    }

    /// <summary>The trimmed value, or null when blank or already in error.</summary>
    public string? Text(string key) => Plan.HasError(key) ? null : Plan.Values[key];

    public DateTime? Date(string key) => Text(key) is { } v ? ParseDate(v) : null;

    public int? Int(string key) =>
        Text(key) is { } v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    public bool? Bool(string key) => Text(key) is { } v ? ParseBool(v) : null;

    /// <summary>
    /// Copies a single-record validator's failures onto the row, by property name → column key.
    /// A column that already has a (more specific) error keeps only that one.
    /// </summary>
    public void AddValidation(ValidationResult result, IReadOnlyDictionary<string, string> propertyToColumn)
    {
        foreach (var failure in result.Errors)
        {
            var key = propertyToColumn.TryGetValue(failure.PropertyName, out var k) ? k : null;
            if (key is not null && Plan.HasError(key)) continue;
            var header = key is null ? null : _definition.Column(key)?.Header;
            var message = header is null
                ? failure.ErrorMessage
                : failure.ErrorMessage.Replace($"'{SplitWords(failure.PropertyName)}'", header, StringComparison.Ordinal);
            Plan.Error(key, message);
        }
    }

    public static DateTime? ParseDate(string value)
    {
        if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
            return DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

        // An Excel date cell arrives as its serial day number. 1..73050 is 1900-01-01..2099-12-31.
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is >= 1 and < 73051)
            return DateTime.SpecifyKind(DateTime.FromOADate(Math.Floor(serial)).Date, DateTimeKind.Unspecified);

        return null;
    }

    public static bool? ParseBool(string value) => value.Trim().ToLowerInvariant() switch
    {
        "yes" or "y" or "true" or "1" => true,
        "no" or "n" or "false" or "0" => false,
        _ => null
    };

    public static bool IsEmail(string value)
    {
        if (value.Length > 200 || value.Contains(' ')) return false;
        try
        {
            var address = new MailAddress(value);
            var at = value.LastIndexOf('@');
            return address.Address == value && at > 0 && value.IndexOf('.', at) > at + 1 && !value.EndsWith('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>FluentValidation's default display name: "DateOfBirth" → "Date Of Birth".</summary>
    private static string SplitWords(string propertyName)
    {
        var chars = new List<char>();
        for (var i = 0; i < propertyName.Length; i++)
        {
            if (i > 0 && char.IsUpper(propertyName[i]) && !char.IsUpper(propertyName[i - 1])) chars.Add(' ');
            chars.Add(propertyName[i]);
        }
        return new string(chars.ToArray());
    }
}
