using Microsoft.AspNetCore.Components;
using MudBlazor;
using SchoolManagement.Web.Services;

namespace SchoolManagement.Web.Shared;

/// <summary>
/// Base class for pages: injects the API client and UI services, and wraps calls so API errors become snackbars.
/// Usage: <c>await Run(async () => items = await Api.GetAsync&lt;...&gt;("api/..."), "Saved.");</c>
/// </summary>
public abstract class AppPage : ComponentBase
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected Session Session { get; set; } = default!;
    [Inject] protected ISnackbar Snackbar { get; set; } = default!;
    [Inject] protected IDialogService Dialogs { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected BrowserFiles Files { get; set; } = default!;

    /// <summary>True while a <see cref="Run"/> call is in flight; bind buttons' Disabled to it.</summary>
    protected bool Busy { get; private set; }

    /// <summary>Validation errors from the last failed call, keyed by API field name (camelCase or PascalCase).</summary>
    protected IReadOnlyDictionary<string, string[]> FieldErrors { get; private set; } = new Dictionary<string, string[]>();

    /// <summary>Runs an API action. Returns false (after showing the error) when it failed.</summary>
    protected async Task<bool> Run(Func<Task> action, string? success = null)
    {
        Busy = true;
        FieldErrors = new Dictionary<string, string[]>();
        StateHasChanged();
        try
        {
            await action();
            if (success is not null) Snackbar.Add(success, Severity.Success);
            return true;
        }
        catch (ApiException ex)
        {
            FieldErrors = ex.Errors;
            Snackbar.Add(ex.Describe(), ex.Status == System.Net.HttpStatusCode.Conflict ? Severity.Warning : Severity.Error);
            return false;
        }
        catch (HttpRequestException)
        {
            Snackbar.Add("The server could not be reached. Check that the API is running.", Severity.Error);
            return false;
        }
        finally
        {
            Busy = false;
            StateHasChanged();
        }
    }

    /// <summary>First validation message for a field, for MudBlazor's ErrorText.</summary>
    protected string? ErrorFor(string field) =>
        FieldErrors.FirstOrDefault(e => string.Equals(e.Key, field, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();

    protected bool HasError(string field) => ErrorFor(field) is not null;

    protected async Task<bool> Confirm(string title, string message, string yes = "Yes")
    {
        var result = await Dialogs.ShowMessageBox(title, message, yesText: yes, cancelText: "Cancel");
        return result == true;
    }

    protected async Task Download(string url, object? body = null)
    {
        await Run(async () => await Files.SaveAsync(await Api.GetFileAsync(url, body)));
    }

    protected async Task OpenFile(string url, object? body = null)
    {
        await Run(async () => await Files.OpenAsync(await Api.GetFileAsync(url, body)));
    }
}
