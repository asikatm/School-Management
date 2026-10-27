using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace SchoolManagement.Web.Services;

/// <summary>An API call failed. <see cref="Message"/> is the problem title; validation errors are listed per field.</summary>
public sealed class ApiException(HttpStatusCode status, string message, IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();

    /// <summary>Title plus every validation message, for a snackbar.</summary>
    public string Describe() => Errors.Count == 0 ? Message : Message + " " + string.Join(" ", Errors.SelectMany(e => e.Value));
}

/// <summary>A file the API returned (export, document, printable page).</summary>
public sealed record ApiFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Typed calls to the School Management API. Adds the bearer token, refreshes it once on 401, and turns
/// RFC 7807 problem responses into <see cref="ApiException"/>.
/// </summary>
public sealed class ApiClient(HttpClient http, Session session, NavigationManager nav)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public const long MaxUploadBytes = 10 * 1024 * 1024;

    public Task<T> GetAsync<T>(string url) => SendAsync<T>(HttpMethod.Get, url, null);
    public Task<T> PostAsync<T>(string url, object? body = null) => SendAsync<T>(HttpMethod.Post, url, body);
    public Task<T> PutAsync<T>(string url, object? body = null) => SendAsync<T>(HttpMethod.Put, url, body);
    public Task<T> PatchAsync<T>(string url, object? body = null) => SendAsync<T>(HttpMethod.Patch, url, body);

    public Task PostAsync(string url, object? body = null) => SendAsync(HttpMethod.Post, url, body);
    public Task PutAsync(string url, object? body = null) => SendAsync(HttpMethod.Put, url, body);
    public Task PatchAsync(string url, object? body = null) => SendAsync(HttpMethod.Patch, url, body);
    public Task DeleteAsync(string url) => SendAsync(HttpMethod.Delete, url, null);

    /// <summary>Multipart upload. <paramref name="fields"/> become form fields; the file (optional) goes in <paramref name="fileField"/>.</summary>
    public async Task<T> UploadAsync<T>(string url, IBrowserFile? file, string fileField = "file", IDictionary<string, string?>? fields = null)
    {
        var response = await SendRawAsync(() =>
        {
            var form = new MultipartFormDataContent();
            foreach (var (key, value) in fields ?? new Dictionary<string, string?>())
                if (value is not null) form.Add(new StringContent(value), key);
            if (file is not null)
            {
                var stream = new StreamContent(file.OpenReadStream(MaxUploadBytes));
                stream.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType);
                form.Add(stream, fileField, file.Name);
            }
            return new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        });
        return await ReadAsync<T>(response);
    }

    public async Task UploadAsync(string url, IBrowserFile file, string fileField = "file", IDictionary<string, string?>? fields = null) =>
        await UploadAsync<JsonElement?>(url, file, fileField, fields);

    /// <summary>Downloads a file (GET, or POST when <paramref name="body"/> is given).</summary>
    public async Task<ApiFile> GetFileAsync(string url, object? body = null, HttpMethod? method = null)
    {
        var response = await SendRawAsync(() => Build(method ?? (body is null ? HttpMethod.Get : HttpMethod.Post), url, body));
        await EnsureSuccessAsync(response);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var type = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var name = response.Content.Headers.ContentDisposition?.FileNameStar
                   ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                   ?? "download";
        return new ApiFile(bytes, type, name);
    }

    /// <summary>Builds a query string, skipping null and empty values.</summary>
    public static string Query(string path, params (string Key, object? Value)[] values)
    {
        var parts = values
            .Where(v => v.Value is not null && !(v.Value is string s && string.IsNullOrWhiteSpace(s)))
            .Select(v => $"{Uri.EscapeDataString(v.Key)}={Uri.EscapeDataString(Format(v.Value!))}");
        var query = string.Join("&", parts);
        return query.Length == 0 ? path : $"{path}?{query}";
    }

    private static string Format(object value) => value switch
    {
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ss"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body)
    {
        var response = await SendRawAsync(() => Build(method, url, body));
        return await ReadAsync<T>(response);
    }

    private async Task SendAsync(HttpMethod method, string url, object? body)
    {
        var response = await SendRawAsync(() => Build(method, url, body));
        await EnsureSuccessAsync(response);
    }

    private static HttpRequestMessage Build(HttpMethod method, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        return request;
    }

    private async Task<HttpResponseMessage> SendRawAsync(Func<HttpRequestMessage> build)
    {
        var token = session.AccessToken;
        var request = build();
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized && token is not null)
        {
            if (await session.RefreshAsync(token))
            {
                var retry = build();
                retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                response = await http.SendAsync(retry);
            }
            else
            {
                nav.NavigateTo("login", replace: true);
            }
        }
        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        await EnsureSuccessAsync(response);
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw await ReadErrorAsync(response);
    }

    public static async Task<ApiException> ReadErrorAsync(HttpResponseMessage response)
    {
        var fallback = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
            HttpStatusCode.Forbidden => "You do not have permission to perform this action.",
            HttpStatusCode.NotFound => "The record was not found.",
            HttpStatusCode.TooManyRequests => "Too many requests. Wait a minute and try again.",
            _ => $"Request failed ({(int)response.StatusCode})."
        };
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>(Json);
            if (problem is null) return new ApiException(response.StatusCode, fallback);
            var title = string.IsNullOrWhiteSpace(problem.Title) || problem.Errors is { Count: > 0 } && problem.Title.StartsWith("One or more validation")
                ? (problem.Errors is { Count: > 0 } ? "Please correct the highlighted fields." : fallback)
                : problem.Title;
            return new ApiException(response.StatusCode, title, problem.Errors);
        }
        catch
        {
            return new ApiException(response.StatusCode, fallback);
        }
    }

    private sealed record Problem(string? Title, int? Status, Dictionary<string, string[]>? Errors);
}
