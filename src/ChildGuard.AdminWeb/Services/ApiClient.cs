using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ChildGuard.AdminWeb.Services;

/// <summary>
/// Thin JSON HTTP wrapper around the ChildGuard.Api. AdminWeb never touches the
/// database directly — every read/write goes through this, matching the
/// Admin Web → API → MySQL architecture.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public ApiClient(HttpClient http) => _http = http;

    public void SetBearerToken(string token) =>
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public async Task<ApiResult<T>> GetAsync<T>(string url, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(url, ct);
        return await ReadResultAsync<T>(response, ct);
    }

    public async Task<ApiResult<T>> PostAsync<T>(string url, object body, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(url, body, JsonOptions, ct);
        return await ReadResultAsync<T>(response, ct);
    }

    public async Task<ApiResult<object?>> PostAsync(string url, object body, CancellationToken ct = default) =>
        await PostAsync<object?>(url, body, ct);

    public async Task<ApiResult<T>> PutAsync<T>(string url, object body, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(url, body, JsonOptions, ct);
        return await ReadResultAsync<T>(response, ct);
    }

    public async Task<ApiResult<T>> PatchAsync<T>(string url, object body, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync(url, body, JsonOptions, ct);
        return await ReadResultAsync<T>(response, ct);
    }

    public async Task<ApiResult<T>> DeleteAsync<T>(string url, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync(url, ct);
        return await ReadResultAsync<T>(response, ct);
    }

    private static async Task<ApiResult<T>> ReadResultAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(raw)) return ApiResult<T>.Ok(default!);
            try
            {
                var value = JsonSerializer.Deserialize<T>(raw, JsonOptions);
                return ApiResult<T>.Ok(value!);
            }
            catch
            {
                return ApiResult<T>.Ok(default!);
            }
        }

        var message = TryExtractMessage(raw) ?? $"Request failed ({(int)response.StatusCode}).";
        return ApiResult<T>.Fail(message, (int)response.StatusCode);
    }

    private static string? TryExtractMessage(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("message", out var m)) return m.GetString();
            if (doc.RootElement.TryGetProperty("errors", out var errs))
                return string.Join(" ", errs.EnumerateArray().Select(e => e.TryGetProperty("errorMessage", out var em) ? em.GetString() : e.ToString()));
        }
        catch { /* not JSON — fall through */ }
        return null;
    }
}

public class ApiResult<T>
{
    public bool Succeeded { get; private init; }
    public T? Value { get; private init; }
    public string? Error { get; private init; }
    public int StatusCode { get; private init; }

    public static ApiResult<T> Ok(T value) => new() { Succeeded = true, Value = value, StatusCode = 200 };
    public static ApiResult<T> Fail(string error, int statusCode) => new() { Succeeded = false, Error = error, StatusCode = statusCode };
}
