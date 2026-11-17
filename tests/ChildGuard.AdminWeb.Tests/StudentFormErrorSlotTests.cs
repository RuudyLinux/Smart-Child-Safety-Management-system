using System.Net;
using System.Text.RegularExpressions;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>
/// Every field on the student forms has its own error slot, so an API validation error for that field
/// (keyed by the same name) appears beside it rather than in a general message.
/// </summary>
public class StudentFormErrorSlotTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;
    public StudentFormErrorSlotTests(AdminWebFactory factory) => _factory = factory;

    private static HttpResponseMessage StudentApi(HttpRequestMessage r) =>
        r.RequestUri!.AbsolutePath == "/api/students/1"
            ? AdminWebFactory.Json(HttpStatusCode.OK, new
            {
                studentId = 1, fullName = "Mira Shah", dateOfBirth = new DateTime(2016, 4, 21), grade = "5A", section = "B",
                admissionNumber = "ADM2026-0001", gender = "Female", status = "Active", guardians = Array.Empty<object>(),
                recentAttendance = Array.Empty<object>(), recentJourney = Array.Empty<object>()
            })
            : new HttpResponseMessage(HttpStatusCode.NotFound);

    /// <summary>Names of the fields inside the first form posting to <paramref name="action"/>.</summary>
    private static IReadOnlyList<string> FieldNames(string html, string action)
    {
        var form = Regex.Match(html, $"<form[^>]*action=\"{Regex.Escape(action)}\"[\\s\\S]*?</form>").Value;
        Assert.False(string.IsNullOrEmpty(form), $"form {action} not found");
        return Regex.Matches(form, "<(?:input|select|textarea)[^>]*\\sname=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(n => n != "__RequestVerificationToken")
            .Distinct()
            .ToList();
    }

    [Fact]
    public async Task EditForm_EveryFieldHasAnErrorSlot()
    {
        var client = await _factory.SignedInClientAsync(StudentApi);
        var html = await client.GetStringAsync("/Students/Edit/1");
        var names = FieldNames(html, "/Students/Edit/1");
        Assert.Contains("AdmissionNumber", names);
        foreach (var name in names)
            Assert.Contains($"data-valmsg-for=\"{name}\"", html);
    }

    [Fact]
    public async Task CreateForm_EveryFieldHasAnErrorSlot()
    {
        var client = await _factory.SignedInClientAsync(StudentApi);
        var html = await client.GetStringAsync("/Students/Create");
        foreach (var name in FieldNames(html, "/Students/Create"))
            Assert.Contains($"data-valmsg-for=\"{name}\"", html);
    }
}
