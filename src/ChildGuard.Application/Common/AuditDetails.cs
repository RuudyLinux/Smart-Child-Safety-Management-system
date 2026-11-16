using System.Text.Json;
using System.Text.Json.Nodes;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Common;

/// <summary>
/// Serializes audit details. Callers pass explicit values only; as a second line of defence any property whose name
/// suggests a credential or biometric value is replaced with "[redacted]", whatever its depth.
/// </summary>
public static class AuditDetails
{
    public const int MaxLength = 4000;
    public const string Redacted = "[redacted]";

    private static readonly string[] SensitiveNameParts =
        { "password", "token", "secret", "apikey", "embedding", "hash", "otp", "ticket", "base64", "image" };

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static bool IsSensitiveName(string name)
    {
        var n = name.ToLowerInvariant();
        return SensitiveNameParts.Any(n.Contains);
    }

    public static string? Serialize(object? details)
    {
        if (details is null) return null;
        var node = JsonSerializer.SerializeToNode(details, details.GetType(), Options);
        Redact(node);
        var json = node?.ToJsonString(Options);
        if (json is null) return null;
        return json.Length <= MaxLength ? json : JsonSerializer.Serialize(new { note = "Details exceeded the audit size limit and were omitted." }, Options);
    }

    private static void Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsSensitiveName(key)) obj[key] = Redacted;
                    else Redact(obj[key]);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) Redact(item);
                break;
        }
    }

    /// <summary>Field-by-field changes between two values, as { field: { from, to } }; empty when nothing changed.</summary>
    public static Dictionary<string, object?> Changes(params (string Field, object? From, object? To)[] fields) =>
        fields.Where(f => !Equals(f.From, f.To))
              .ToDictionary(f => f.Field, f => (object?)new { from = f.From, to = f.To });

    /// <summary>Which address parts changed. Values are left out: the address stays on the record, not in the trail.</summary>
    public static List<string> ChangedAddressParts(PostalAddress? before, PostalAddress? after)
    {
        string? P(PostalAddress? a, Func<PostalAddress, string?> f) => a is null ? null : f(a);
        var parts = new (string Name, Func<PostalAddress, string?> Get)[]
        {
            ("line1", a => a.Line1), ("line2", a => a.Line2), ("locality", a => a.Locality), ("landmark", a => a.Landmark),
            ("city", a => a.City), ("district", a => a.District), ("state", a => a.State),
            ("postalCode", a => a.PostalCode), ("country", a => a.Country)
        };
        return parts.Where(p => P(before, p.Get) != P(after, p.Get)).Select(p => p.Name).ToList();
    }
}
