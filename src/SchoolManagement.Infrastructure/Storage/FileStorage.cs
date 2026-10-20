using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SchoolManagement.Infrastructure.Common;

namespace SchoolManagement.Infrastructure.Storage;

public class StorageOptions
{
    /// <summary>Document vault folder, outside any web root. Relative paths resolve against the API content root.</summary>
    public string RootPath { get; set; } = "../../storage";
    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;
    public bool EnableDefenderScan { get; set; } = true;
}

public sealed record StoredFile(string Path, string FileName, string ContentType, long SizeBytes, string Sha256);

public interface IFileStorage
{
    /// <summary>Type-checks, scans and saves a file under <paramref name="folder"/>; returns its vault-relative path.</summary>
    Task<StoredFile> SaveAsync(Stream content, string folder, string fileName, string contentType, CancellationToken ct);
    Stream OpenRead(string path);
    void Delete(string path);
}

/// <summary>SIS-03: local document vault. Content is sniffed by signature, not trusted from the client.</summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options, IFileScanner scanner) : IFileStorage
{
    private static readonly (string Ext, string ContentType, byte[] Magic)[] Allowed =
    [
        (".pdf", "application/pdf", "%PDF"u8.ToArray()),
        (".jpg", "image/jpeg", [0xFF, 0xD8, 0xFF]),
        (".png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        (".webp", "image/webp", "RIFF"u8.ToArray()),
    ];

    private string Root => options.Value.RootPath;

    public async Task<StoredFile> SaveAsync(Stream content, string folder, string fileName, string contentType, CancellationToken ct)
    {
        var max = options.Value.MaxFileBytes;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length == 0) throw new DomainException("The file is empty.", "file_empty");
        if (buffer.Length > max) throw new DomainException($"Files may be at most {max / (1024 * 1024)} MB.", "file_too_large");

        var bytes = buffer.ToArray();
        var type = Detect(bytes) ?? throw new DomainException("Only PDF, JPEG, PNG or WebP files are accepted.", "file_type");

        var relative = Path.Combine(folder, $"{Guid.NewGuid():N}{type.Ext}").Replace('\\', '/');
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes, ct);

        if (!await scanner.IsCleanAsync(full, ct))
        {
            File.Delete(full);
            throw new DomainException("The file was rejected by the virus scanner.", "file_infected");
        }

        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "file" + type.Ext;
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new StoredFile(relative, safeName, type.ContentType, buffer.Length, sha);
    }

    public Stream OpenRead(string path)
    {
        var full = Resolve(path);
        if (!File.Exists(full)) throw new NotFoundException("File", path);
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
    }

    public void Delete(string path)
    {
        var full = Resolve(path);
        if (File.Exists(full)) File.Delete(full);
    }

    // Keeps every path inside the vault root.
    private string Resolve(string relative)
    {
        var root = Path.GetFullPath(Root);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Invalid storage path.", "file_path");
        return full;
    }

    private static (string Ext, string ContentType, byte[] Magic)? Detect(ReadOnlySpan<byte> bytes)
    {
        foreach (var t in Allowed)
        {
            if (!bytes.StartsWith(t.Magic)) continue;
            if (t.Ext == ".webp" && (bytes.Length < 12 || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8))) continue;
            return t;
        }
        return null;
    }
}
