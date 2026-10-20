using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SchoolManagement.Infrastructure.Storage;

public interface IFileScanner
{
    Task<bool> IsCleanAsync(string fullPath, CancellationToken ct);
}

/// <summary>Scans uploads with Microsoft Defender (MpCmdRun). Disabled or unavailable scanner = file accepted, with a warning.</summary>
public sealed class DefenderFileScanner(IOptions<StorageOptions> options, ILogger<DefenderFileScanner> logger) : IFileScanner
{
    public async Task<bool> IsCleanAsync(string fullPath, CancellationToken ct)
    {
        if (!options.Value.EnableDefenderScan) return true;

        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        if (!OperatingSystem.IsWindows() || !File.Exists(exe))
        {
            logger.LogWarning("Defender scan is enabled but MpCmdRun.exe was not found; {File} was not scanned", fullPath);
            return true;
        }

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var a in new[] { "-Scan", "-ScanType", "3", "-File", fullPath, "-DisableRemediation" }) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync(ct);
        // 0 = no threat, 2 = threat found; anything else is a scanner error.
        if (process.ExitCode == 2) return false;
        if (process.ExitCode != 0) logger.LogWarning("Defender scan of {File} exited with {Code}", fullPath, process.ExitCode);
        return true;
    }
}
