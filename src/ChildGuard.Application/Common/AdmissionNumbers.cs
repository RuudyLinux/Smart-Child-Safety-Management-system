namespace ChildGuard.Application.Common;

/// <summary>
/// Admission numbers are assigned by the system when a student is created without one: "ADM" + the school year
/// of enrollment + the student id, for example ADM202600042. The id makes it unique; the year keeps it readable.
/// A number entered by hand earlier that happens to match gets a "-2", "-3"… suffix instead.
/// </summary>
public static class AdmissionNumbers
{
    public static string For(int studentId, DateOnly enrolledOn) => $"ADM{enrolledOn.Year}{studentId:D5}";

    public static async Task<string> NextFreeAsync(int studentId, DateOnly enrolledOn, Func<string, Task<bool>> taken)
    {
        var first = For(studentId, enrolledOn);
        var candidate = first;
        for (var n = 2; await taken(candidate); n++) candidate = $"{first}-{n}";
        return candidate;
    }
}
