using System.Net.Http.Headers;
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

    /// <summary>
    /// Forwards a browser file upload to the API as multipart, unchanged. AdminWeb
    /// deliberately does not inspect or re-encode the bytes: the API is the trust
    /// boundary that decides whether they are an acceptable image.
    /// </summary>
    public async Task<ApiResult<T>> PostFileAsync<T>(
        string url, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var filePart = new StreamContent(content);
        filePart.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(filePart, "file", fileName);

        var response = await _http.PostAsync(url, form, ct);
        return await ReadResultAsync<T>(response, ct);
    }

    /// <summary>
    /// Fetches a binary resource (a profile photo) so AdminWeb can re-serve it to
    /// the signed-in admin's browser. The browser never talks to the API directly,
    /// so the API's bearer token stays server-side.
    /// </summary>
    public async Task<ApiResult<(byte[] Data, string ContentType)>> GetBinaryAsync(string url, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return ApiResult<(byte[], string)>.Fail($"Request failed ({(int)response.StatusCode}).", (int)response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return ApiResult<(byte[], string)>.Ok((bytes, contentType));
    }

    /// <summary>
    /// Fetches a generated download (import template, error report) with the file name the API
    /// chose, so AdminWeb can re-serve it to the admin's browser unchanged.
    /// </summary>
    public async Task<ApiResult<(byte[] Data, string ContentType, string FileName)>> GetFileAsync(
        string url, string fallbackFileName, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            return ApiResult<(byte[], string, string)>.Fail(
                TryExtractMessage(raw) ?? $"Request failed ({(int)response.StatusCode}).", (int)response.StatusCode);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"');
        return ApiResult<(byte[], string, string)>.Ok((bytes, contentType, string.IsNullOrWhiteSpace(fileName) ? fallbackFileName : fileName));
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
            catch (JsonException)
            {
                return ApiResult<T>.Fail("The server returned a response that could not be read.", (int)response.StatusCode);
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

/// <summary>
/// The same API client with a longer timeout, for the few calls that legitimately run long
/// (a bulk import commit hashes a password and sends an email per guardian). Every other call
/// keeps the normal timeout.
/// </summary>
public sealed class LongRunningApiClient : ApiClient
{
    public LongRunningApiClient(HttpClient http) : base(http) { }
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
