using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using SchoolManagement.Web.Models;

namespace SchoolManagement.Web.Services;

/// <summary>
/// The signed-in user: tokens (kept in localStorage so a reload stays signed in), profile and permissions.
/// Permission grants from the API look like "students.view@{campusId}" or "students.view@*".
/// </summary>
public sealed class Session(IJSRuntime js, HttpClient http)
{
    private const string StorageKey = "sm.session";
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private Tokens? tokens;
    private bool restored;

    public MeResponse? Me { get; private set; }
    public bool IsSignedIn => tokens is not null && Me is not null;
    public bool MustChangePassword => Me?.MustChangePassword ?? false;
    public string? AccessToken => tokens?.AccessToken;

    public event Action? Changed;

    public bool Has(string permission) =>
        Me?.Permissions.Any(p => p == permission || p.StartsWith(permission + "@", StringComparison.Ordinal)) ?? false;

    public bool HasAny(params string[] permissions) => permissions.Any(Has);

    public async Task RestoreAsync()
    {
        if (restored) return;
        restored = true;
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(json)) return;
            tokens = JsonSerializer.Deserialize<Tokens>(json);
            if (tokens is null) return;
            if (!await LoadMeAsync()) await ClearAsync();
        }
        catch
        {
            await ClearAsync();
        }
    }

    /// <summary>Returns null on success or the error message to show.</summary>
    public async Task<string?> LoginAsync(string login, string password)
    {
        var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(login, password), ApiClient.Json);
        if (!response.IsSuccessStatusCode)
            return (await ApiClient.ReadErrorAsync(response)).Message;

        var result = (await response.Content.ReadFromJsonAsync<LoginResponse>(ApiClient.Json))!;
        await SaveAsync(new Tokens(result.AccessToken, result.AccessTokenExpiresAtUtc, result.RefreshToken));
        return await LoadMeAsync() ? null : "Signed in, but the profile could not be loaded.";
    }

    public async Task LogoutAsync()
    {
        if (tokens is not null)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout")
                {
                    Content = JsonContent.Create(new RefreshRequest(tokens.RefreshToken), options: ApiClient.Json)
                };
                request.Headers.Authorization = new("Bearer", tokens.AccessToken);
                await http.SendAsync(request);
            }
            catch
            {
                // Signing out locally is enough when the API cannot be reached.
            }
        }
        await ClearAsync();
    }

    /// <summary>Called after the password was changed; the API revoked every refresh token.</summary>
    public async Task SignInAgainAsync(string login, string password)
    {
        await ClearAsync();
        await LoginAsync(login, password);
    }

    public async Task<bool> RefreshAsync(string? failedAccessToken)
    {
        await refreshLock.WaitAsync();
        try
        {
            // Another request already refreshed while this one waited.
            if (tokens is null) return false;
            if (failedAccessToken is not null && tokens.AccessToken != failedAccessToken) return true;

            var response = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(tokens.RefreshToken), ApiClient.Json);
            if (!response.IsSuccessStatusCode)
            {
                await ClearAsync();
                return false;
            }
            var result = (await response.Content.ReadFromJsonAsync<LoginResponse>(ApiClient.Json))!;
            await SaveAsync(new Tokens(result.AccessToken, result.AccessTokenExpiresAtUtc, result.RefreshToken));
            return true;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    /// <summary>Re-reads profile and permissions, e.g. after the user's roles changed.</summary>
    public async Task<bool> LoadMeAsync()
    {
        if (tokens is null) return false;
        var response = await SendMeAsync();
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshAsync(tokens.AccessToken))
            response = await SendMeAsync();
        if (!response.IsSuccessStatusCode) return false;

        Me = await response.Content.ReadFromJsonAsync<MeResponse>(ApiClient.Json);
        Changed?.Invoke();
        return Me is not null;
    }

    private async Task<HttpResponseMessage> SendMeAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        request.Headers.Authorization = new("Bearer", tokens!.AccessToken);
        return await http.SendAsync(request);
    }

    private async Task SaveAsync(Tokens value)
    {
        tokens = value;
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(value));
    }

    private async Task ClearAsync()
    {
        tokens = null;
        Me = null;
        try { await js.InvokeVoidAsync("localStorage.removeItem", StorageKey); } catch { /* storage unavailable */ }
        Changed?.Invoke();
    }

    private sealed record Tokens(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken);
}
