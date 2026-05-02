using System.IO;
using Trnscrbr.Models;

namespace Trnscrbr.Services;

public sealed class LocalModeRepairService
{
    private readonly LocalWhisperToolDownloadService _toolDownload;
    private readonly LocalModelDownloadService _modelDownload;

    public LocalModeRepairService(
        LocalWhisperToolDownloadService toolDownload,
        LocalModelDownloadService modelDownload)
    {
        _toolDownload = toolDownload;
        _modelDownload = modelDownload;
    }

    public async Task<LocalModeRepairResult> RepairAsync(
        AppSettings settings,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<LocalModeRepairStep>();
        var preset = _modelDownload.FindPreset(settings.LocalWhisperModelPath, settings.LocalWhisperModelPresetId)
            ?? LocalModelDownloadService.Presets.First(candidate => candidate.Id == "small");

        progress?.Report("Checking whisper.cpp CLI...");
        if (!File.Exists(settings.LocalWhisperExecutablePath))
        {
            var cli = await _toolDownload.TryUseExistingLatestX64Async(cancellationToken);
            if (cli is null)
            {
                progress?.Report("Installing whisper.cpp CLI...");
                cli = await _toolDownload.DownloadLatestX64Async(downloadProgress, cancellationToken);
                steps.Add(new LocalModeRepairStep("CLI", "Installed whisper.cpp CLI."));
            }
            else
            {
                steps.Add(new LocalModeRepairStep("CLI", "Reused installed whisper.cpp CLI."));
            }

            settings.LocalWhisperExecutablePath = cli.ExecutablePath;
            settings.LocalWhisperCliVersion = cli.Version;
        }
        else
        {
            steps.Add(new LocalModeRepairStep("CLI", "Already configured."));
        }

        progress?.Report($"Checking {preset.DisplayName} model...");
        var keepConfiguredModel = await CanKeepConfiguredModelAsync(settings, preset, cancellationToken);
        if (!keepConfiguredModel)
        {
            var model = await _modelDownload.TryUseExistingAsync(preset, cancellationToken);
            if (model is null)
            {
                progress?.Report($"Downloading {preset.DisplayName}...");
                model = await _modelDownload.DownloadAsync(preset, downloadProgress, cancellationToken);
                steps.Add(new LocalModeRepairStep("Model", $"Downloaded and verified {preset.DisplayName}."));
            }
            else
            {
                steps.Add(new LocalModeRepairStep("Model", $"Reused verified {preset.DisplayName}."));
            }

            settings.LocalWhisperModelPath = model.ModelPath;
            settings.LocalWhisperModelPresetId = model.PresetId;
        }
        else
        {
            steps.Add(new LocalModeRepairStep("Model", "Already configured and verified."));
        }

        settings.ProviderMode = "Local mode";
        settings.ProviderName = "Local";
        settings.ActiveEngine = "Local Whisper";
        settings.LocalSetupSource = "Repair Local Mode";
        settings.LocalSetupCompletedAt = DateTimeOffset.Now;
        steps.Add(new LocalModeRepairStep("Provider", "Local mode is active."));

        return new LocalModeRepairResult("Local mode repaired.", steps);
    }

    private async Task<bool> CanKeepConfiguredModelAsync(
        AppSettings settings,
        LocalModelPreset preset,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.LocalWhisperModelPath))
        {
            return false;
        }

        var configuredPreset = _modelDownload.FindPreset(settings.LocalWhisperModelPath, settings.LocalWhisperModelPresetId);
        return configuredPreset is null
            || await _modelDownload.VerifyPresetAsync(settings.LocalWhisperModelPath, configuredPreset, cancellationToken);
    }
}

public sealed record LocalModeRepairResult(string Message, IReadOnlyList<LocalModeRepairStep> Steps);

public sealed record LocalModeRepairStep(string Name, string Detail);
