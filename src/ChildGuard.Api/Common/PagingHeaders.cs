using ChildGuard.Application.DTOs;

namespace ChildGuard.Api.Common;

/// <summary>Paged list responses keep the list's JSON shape; the total travels in a header.</summary>
public static class PagingHeaders
{
    public const string TotalCount = "X-Total-Count";

    public static void SetTotal(HttpResponse response, int total) =>
        response.Headers[TotalCount] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Parses an optional status filter; false for a value that is not a list status.</summary>
    public static bool TryParseStatus(string? value, out ListStatusFilter? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Enum.TryParse<ListStatusFilter>(value.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)) return false;
        status = parsed;
        return true;
    }
}
