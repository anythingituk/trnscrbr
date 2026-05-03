using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Trnscrbr.Services;

public sealed class LocalHardwareProfileService
{
    public async Task<LocalHardwareProfile> DetectAsync(CancellationToken cancellationToken = default)
    {
        var logicalProcessors = Environment.ProcessorCount;
        var memoryGb = GetInstalledMemoryGb();
        var gpuNames = await DetectGpuNamesAsync(cancellationToken);
        var hasNvidiaCuda = gpuNames.Any(name => name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            || await HasNvidiaSmiAsync(cancellationToken);

        var guidance = BuildGuidance(logicalProcessors, memoryGb, gpuNames, hasNvidiaCuda);
        return new LocalHardwareProfile(logicalProcessors, memoryGb, gpuNames, hasNvidiaCuda, guidance);
    }

    private static string BuildGuidance(int logicalProcessors, double memoryGb, IReadOnlyList<string> gpuNames, bool hasNvidiaCuda)
    {
        var memoryText = memoryGb > 0 ? $"{memoryGb:0.#} GB RAM" : "unknown RAM";
        var gpuText = gpuNames.Count > 0 ? string.Join(", ", gpuNames) : "no dedicated GPU detected";

        if (hasNvidiaCuda)
        {
            return $"This PC has {logicalProcessors} CPU threads, {memoryText}, and {gpuText}. Built-in local AI currently uses the CPU engine, so Small is still the safest default until GPU engine support is added.";
        }

        if (memoryGb >= 16 && logicalProcessors >= 8)
        {
            return $"This PC has {logicalProcessors} CPU threads and {memoryText}. Small should be responsive; Medium may be usable for longer jobs. Large can still be slow.";
        }

        if (memoryGb >= 8 && logicalProcessors >= 4)
        {
            return $"This PC has {logicalProcessors} CPU threads and {memoryText}. Small is recommended. Medium and Large may take a long time.";
        }

        return $"This PC has {logicalProcessors} CPU threads and {memoryText}. Use Small for local AI; larger models are likely to feel slow.";
    }

    private static double GetInstalledMemoryGb()
    {
        try
        {
            var status = new MEMORYSTATUSEX();
            status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            return GlobalMemoryStatusEx(ref status)
                ? status.ullTotalPhys / 1024d / 1024d / 1024d
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static async Task<IReadOnlyList<string>> DetectGpuNamesAsync(CancellationToken cancellationToken)
    {
        var output = await RunProcessAsync(
            "powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name }\"",
            cancellationToken);

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
    }

    private static async Task<bool> HasNvidiaSmiAsync(CancellationToken cancellationToken)
    {
        var output = await RunProcessAsync(
            "nvidia-smi.exe",
            "--query-gpu=name --format=csv,noheader",
            cancellationToken);
        return !string.IsNullOrWhiteSpace(output);
    }

    private static async Task<string> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return string.Empty;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return await outputTask;
        }
        catch
        {
            return string.Empty;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}

public sealed record LocalHardwareProfile(
    int LogicalProcessors,
    double MemoryGb,
    IReadOnlyList<string> GpuNames,
    bool HasNvidiaCuda,
    string Guidance);
