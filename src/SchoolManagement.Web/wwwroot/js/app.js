// Small browser helpers called from Blazor (Services/BrowserFiles.cs).
window.smFiles = {
    // Saves bytes the API returned (exports, documents) as a download.
    download: function (fileName, contentType, base64) {
        const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
        const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 10000);
    },
    // Opens bytes in a new tab (printable HTML pages, photos, PDFs). Falls back to a download when the
    // browser blocks the pop-up (the click's user activation can expire while the file is fetched).
    open: function (contentType, base64, fileName) {
        const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
        const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
        const win = window.open(url, '_blank');
        if (!win) {
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName || 'download';
            document.body.appendChild(a);
            a.click();
            a.remove();
        }
        setTimeout(() => URL.revokeObjectURL(url), 60000);
    }
};
