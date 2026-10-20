using Microsoft.JSInterop;

namespace SchoolManagement.Web.Services;

/// <summary>Hands files from the API to the browser (wwwroot/js/app.js).</summary>
public sealed class BrowserFiles(IJSRuntime js)
{
    public ValueTask SaveAsync(ApiFile file) =>
        js.InvokeVoidAsync("smFiles.download", file.FileName, file.ContentType, Convert.ToBase64String(file.Content));

    /// <summary>Opens printable HTML, images and PDFs in a new tab.</summary>
    public ValueTask OpenAsync(ApiFile file) =>
        js.InvokeVoidAsync("smFiles.open", file.ContentType, Convert.ToBase64String(file.Content), file.FileName);
}
