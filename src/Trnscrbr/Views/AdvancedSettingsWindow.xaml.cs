using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Win32;
using Trnscrbr.Models;
using Trnscrbr.Services;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Views;

public partial class AdvancedSettingsWindow : Window
{
    private readonly AppStateViewModel _state;
    private readonly AppSettingsStore _settingsStore;
    private readonly CredentialStore _credentialStore;
    private readonly OpenAiProviderService _openAiProvider;
    private readonly AudioCaptureService _audioCapture;
    private readonly DiagnosticLogService _diagnosticLog;
    private readonly UsageStatsService _usageStats;
    private readonly SettingsImportExportService _settingsImportExport;
    private readonly LocalModelDiscoveryService _localModelDiscovery = new();
    private readonly LocalProviderService _localProvider = new();
    private readonly LocalModelDownloadService _localModelDownload = new();
    private readonly LocalWhisperToolDownloadService _localWhisperToolDownload = new();
    private readonly UpdateCheckService _updateCheck = new();
    private CancellationTokenSource? _modelDownloadCancellation;

    public AdvancedSettingsWindow(
        AppStateViewModel state,
        AppSettingsStore settingsStore,
        CredentialStore credentialStore,
        OpenAiProviderService openAiProvider,
        AudioCaptureService audioCapture,
        DiagnosticLogService diagnosticLog,
        UsageStatsService usageStats,
        SettingsImportExportService settingsImportExport)
    {
        InitializeComponent();
        _state = state;
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        _openAiProvider = openAiProvider;
        _audioCapture = audioCapture;
        _diagnosticLog = diagnosticLog;
        _usageStats = usageStats;
        _settingsImportExport = settingsImportExport;
        DataContext = state;
        ModelPresetComboBox.ItemsSource = LocalModelDownloadService.Presets;
        ModelPresetComboBox.SelectedIndex = 0;
        VocabularyBox.Text = string.Join(Environment.NewLine, state.Settings.CustomVocabulary);
        DiagnosticsBox.Text = _diagnosticLog.ReadRecent();
        UsageBox.Text = _usageStats.FormatSummary(_state.Settings.MonthlyCostWarning);
        CurrentVersionText.Text = $"Current version: {AppInfo.Version}";
        RefreshOverview();
        RefreshLocalModels();
        RefreshLocalModeStatus();
        UpdateApiKeyStatus();
        UpdateProviderModeStatus();
        Closing += (_, args) =>
        {
            args.Cancel = true;
            Persist();
            Hide();
        };
    }

    private void OnboardingComplete_OnClick(object sender, RoutedEventArgs e)
    {
        _state.Settings.OnboardingCompleted = true;
        Persist();
    }

    private async void TestConnection_OnClick(object sender, RoutedEventArgs e)
    {
        ApiKeyStatusText.Text = "Testing OpenAI connection...";
        var result = await _openAiProvider.TestApiKeyAsync(ApiKeyBox.Password);
        ApiKeyStatusText.Text = result.IsSuccess
            ? $"{result.Message} Click Save Key to store this key."
            : result.Message;
    }

    private async void SaveProvider_OnClick(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Password.Trim();
        if (apiKey.Length == 0)
        {
            System.Windows.MessageBox.Show("Enter an API key before saving.", "Trnscrbr");
            return;
        }

        ApiKeyStatusText.Text = "Testing OpenAI connection before save...";
        var result = await _openAiProvider.TestApiKeyAsync(apiKey);
        if (!result.IsSuccess)
        {
            var choice = System.Windows.MessageBox.Show(
                $"{result.Message}\n\nSave this key anyway?",
                "Trnscrbr",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (choice != MessageBoxResult.Yes)
            {
                ApiKeyStatusText.Text = "API key not saved.";
                return;
            }
        }

        _credentialStore.SaveOpenAiApiKey(apiKey);
        ApiKeyBox.Clear();
        _state.Settings.ProviderMode = "Bring your own API key";
        _state.Settings.ProviderName = "OpenAI";
        _state.Settings.ActiveEngine = "OpenAI";
        Persist();
        RefreshOverview();
        UpdateProviderModeStatus();
        RefreshLocalModeStatus();
        ApiKeyStatusText.Text = result.IsSuccess
            ? "OpenAI key saved in Windows Credential Manager."
            : "OpenAI key saved with warning in Windows Credential Manager.";
    }

    private void DeleteKey_OnClick(object sender, RoutedEventArgs e)
    {
        _credentialStore.DeleteOpenAiApiKey();
        ApiKeyBox.Clear();
        _state.Settings.ProviderMode = "Not configured";
        _state.Settings.ActiveEngine = "None";
        Persist();
        RefreshOverview();
        UpdateApiKeyStatus();
        UpdateProviderModeStatus();
        RefreshLocalModeStatus();
    }

    private void CaptureBuffer_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox comboBox)
        {
            return;
        }

        var milliseconds = comboBox.SelectedValue switch
        {
            int intValue => intValue,
            string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
            _ => _state.Settings.CaptureStartupBufferMilliseconds
        };

        _state.Settings.CaptureStartupBufferMilliseconds = milliseconds;
        Persist();
        _audioCapture.ApplyPreBufferSetting();
    }

    private void Settings_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_state.Settings.ProviderMode == "Local mode")
        {
            _state.Settings.ProviderName = "Local";
            _state.Settings.ActiveEngine = "Local Whisper";
        }
        else if (_state.Settings.ProviderMode == "Bring your own API key")
        {
            _state.Settings.ProviderName = "OpenAI";
            _state.Settings.ActiveEngine = "OpenAI";
        }
        else if (_state.Settings.ProviderMode == "Not configured")
        {
            _state.Settings.ProviderName = "OpenAI";
            _state.Settings.ActiveEngine = "None";
        }

        Persist();
        RefreshOverview();
        UpdateProviderModeStatus();
        RefreshLocalModeStatus();
    }

    private void CursorContext_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_state.Settings.CursorContextEnabled)
        {
            Persist();
            return;
        }

        var choice = System.Windows.MessageBox.Show(
            "Cursor context may read nearby text from the active app to improve correction. This can expose private content from the current window and may not work reliably in every app.\n\nEnable cursor context?",
            "Trnscrbr Privacy Warning",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (choice != MessageBoxResult.Yes)
        {
            _state.Settings.CursorContextEnabled = false;
            _state.RaiseSettingsChanged();
            return;
        }

        Persist();
    }

    private void DetectLocalModels_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshLocalModels();
    }

    private void ModelPreset_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ModelPresetComboBox.SelectedItem is not LocalModelPreset preset)
        {
            ModelPresetDescriptionText.Text = string.Empty;
            return;
        }

        ModelPresetDescriptionText.Text = $"{preset.Description} Download: {preset.DiskSize}. Recommended: {preset.RamRecommendation}.";
    }

    private async void QuickSetupLocal_OnClick(object sender, RoutedEventArgs e)
    {
        var preset = LocalModelDownloadService.Presets.First(candidate => candidate.Id == "small");
        var choice = System.Windows.MessageBox.Show(
            $"Free quick setup will install the official x64 whisper.cpp CPU CLI and download {preset.DisplayName} ({preset.DiskSize}).\n\nContinue?",
            "Trnscrbr",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        _modelDownloadCancellation?.Cancel();
        _modelDownloadCancellation?.Dispose();
        _modelDownloadCancellation = new CancellationTokenSource();

        try
        {
            LocalModeStatusText.Text = "Installing whisper.cpp CLI...";
            var cliPath = await _localWhisperToolDownload.DownloadLatestX64Async(
                new Progress<double>(value => LocalModeStatusText.Text = $"Installing whisper.cpp CLI: {value:P0}"),
                _modelDownloadCancellation.Token);

            LocalModeStatusText.Text = $"Downloading {preset.DisplayName}...";
            var modelPath = await _localModelDownload.DownloadAsync(
                preset,
                new Progress<double>(value => LocalModeStatusText.Text = $"Downloading {preset.DisplayName}: {value:P0}"),
                _modelDownloadCancellation.Token);

            _state.Settings.LocalWhisperExecutablePath = cliPath;
            _state.Settings.LocalWhisperModelPath = modelPath;
            _state.Settings.ProviderMode = "Local mode";
            _state.Settings.ProviderName = "Local";
            _state.Settings.ActiveEngine = "Local Whisper";
            Persist();
            RefreshOverview();
            RefreshLocalModels();
            RefreshLocalModeStatus();
            UpdateProviderModeStatus();
            LocalModeStatusText.Text = "Free local mode is ready. Ollama cleanup remains optional.";
        }
        catch (OperationCanceledException)
        {
            LocalModeStatusText.Text = "Free quick setup cancelled. Partial model download was kept so it can resume later.";
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            LocalModeStatusText.Text = $"Free quick setup failed: {ex.Message}";
            _diagnosticLog.Error("Free local quick setup failed", ex);
        }
    }

    private async void DownloadModel_OnClick(object sender, RoutedEventArgs e)
    {
        if (ModelPresetComboBox.SelectedItem is not LocalModelPreset preset)
        {
            LocalModeStatusText.Text = "Choose a model preset first.";
            return;
        }

        _modelDownloadCancellation?.Cancel();
        _modelDownloadCancellation?.Dispose();
        _modelDownloadCancellation = new CancellationTokenSource();

        LocalModeStatusText.Text = $"Downloading {preset.DisplayName}...";
        var progress = new Progress<double>(value =>
        {
            LocalModeStatusText.Text = $"Downloading {preset.DisplayName}: {value:P0}";
        });

        try
        {
            var modelPath = await _localModelDownload.DownloadAsync(
                preset,
                progress,
                _modelDownloadCancellation.Token);

            _state.Settings.LocalWhisperModelPath = modelPath;
            Persist();
            RefreshOverview();
            RefreshLocalModels();
            RefreshLocalModeStatus();
            UpdateProviderModeStatus();
            LocalModeStatusText.Text = $"Downloaded and verified {preset.DisplayName}.";
        }
        catch (OperationCanceledException)
        {
            LocalModeStatusText.Text = "Model download cancelled. Partial download was kept so it can resume later.";
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or InvalidOperationException)
        {
            LocalModeStatusText.Text = $"Model download failed: {ex.Message}";
            _diagnosticLog.Error("Local model download failed", ex, new Dictionary<string, string>
            {
                ["preset"] = preset.Id,
                ["fileName"] = preset.FileName
            });
        }
    }

    private void CancelModelDownload_OnClick(object sender, RoutedEventArgs e)
    {
        _modelDownloadCancellation?.Cancel();
    }

    private void OpenModelsFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_localModelDownload.ModelsDirectory);
        Process.Start(new ProcessStartInfo(_localModelDownload.ModelsDirectory) { UseShellExecute = true });
    }

    private async void InstallWhisperCli_OnClick(object sender, RoutedEventArgs e)
    {
        LocalModeStatusText.Text = "Downloading whisper.cpp CLI...";
        var progress = new Progress<double>(value =>
        {
            LocalModeStatusText.Text = $"Downloading whisper.cpp CLI: {value:P0}";
        });

        try
        {
            var cliPath = await _localWhisperToolDownload.DownloadLatestX64Async(progress);
            _state.Settings.LocalWhisperExecutablePath = cliPath;
            Persist();
            RefreshOverview();
            RefreshLocalModels();
            RefreshLocalModeStatus();
            UpdateProviderModeStatus();
            LocalModeStatusText.Text = "Installed and verified whisper.cpp CLI.";
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            LocalModeStatusText.Text = $"Could not install whisper.cpp CLI: {ex.Message}";
            _diagnosticLog.Error("Whisper CLI install failed", ex);
        }
    }

    private void OpenWhisperToolsFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_localWhisperToolDownload.ToolsDirectory);
        Process.Start(new ProcessStartInfo(_localWhisperToolDownload.ToolsDirectory) { UseShellExecute = true });
    }

    private void RemoveModel_OnClick(object sender, RoutedEventArgs e)
    {
        var modelPath = _state.Settings.LocalWhisperModelPath;
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            LocalModeStatusText.Text = "No configured local model file to remove.";
            return;
        }

        var managedRoot = Path.GetFullPath(_localModelDownload.ModelsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(modelPath);
        if (!fullPath.StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase))
        {
            LocalModeStatusText.Text = "Only models downloaded into Trnscrbr's managed model folder can be removed here.";
            return;
        }

        var choice = System.Windows.MessageBox.Show(
            $"Remove {Path.GetFileName(fullPath)}?",
            "Trnscrbr",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            File.Delete(fullPath);
            _state.Settings.LocalWhisperModelPath = string.Empty;
            if (_state.Settings.ProviderMode == "Local mode")
            {
                _state.Settings.ProviderMode = "Not configured";
                _state.Settings.ActiveEngine = "None";
            }

            Persist();
            RefreshOverview();
            RefreshLocalModels();
            RefreshLocalModeStatus();
            UpdateProviderModeStatus();
            LocalModeStatusText.Text = "Removed downloaded model.";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException)
        {
            LocalModeStatusText.Text = $"Could not remove model: {ex.Message}";
            _diagnosticLog.Error("Local model removal failed", ex);
        }
    }

    private async void DetectOllamaModels_OnClick(object sender, RoutedEventArgs e)
    {
        LocalModeStatusText.Text = "Checking Ollama models...";

        try
        {
            var models = await _localProvider.ListLocalLlmModelsAsync(_state.Settings.LocalLlmEndpoint);
            if (models.Count == 0)
            {
                LocalModeStatusText.Text = "Ollama is reachable, but no chat models were found. Pull a model or leave cleanup model blank.";
                return;
            }

            LocalModeStatusText.Text = $"Ollama models: {string.Join(", ", models)}";
            if (string.IsNullOrWhiteSpace(_state.Settings.LocalLlmModel))
            {
                _state.Settings.LocalLlmModel = models[0];
                Persist();
                RefreshOverview();
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            LocalModeStatusText.Text = $"Could not reach Ollama at {_state.Settings.LocalLlmEndpoint}: {ex.Message}";
        }
    }

    private void BrowseWhisperExecutable_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose whisper.cpp executable",
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(_state.Settings.LocalWhisperExecutablePath)
                ? "whisper-cli.exe"
                : System.IO.Path.GetFileName(_state.Settings.LocalWhisperExecutablePath)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _state.Settings.LocalWhisperExecutablePath = dialog.FileName;
        Persist();
        RefreshOverview();
        RefreshLocalModeStatus();
        UpdateProviderModeStatus();
    }

    private void BrowseWhisperModel_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose Whisper model",
            Filter = "Whisper model files (*.bin;*.gguf)|*.bin;*.gguf|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _state.Settings.LocalWhisperModelPath = dialog.FileName;
        Persist();
        RefreshOverview();
        RefreshLocalModeStatus();
        UpdateProviderModeStatus();
    }

    private async void TestLocalSetup_OnClick(object sender, RoutedEventArgs e)
    {
        LocalModeStatusText.Text = "Testing local setup...";
        var result = await _localProvider.TestLocalConfigurationAsync(_state);
        LocalModeStatusText.Text = result.Message;
    }

    private async void RunLocalSmokeTest_OnClick(object sender, RoutedEventArgs e)
    {
        LocalModeStatusText.Text = "Running Whisper runtime smoke test...";
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            var result = await _localProvider.RunWhisperSmokeTestAsync(_state, timeout.Token);
            LocalModeStatusText.Text = result.Message;
        }
        catch (OperationCanceledException)
        {
            LocalModeStatusText.Text = "Whisper runtime smoke test timed out after 2 minutes.";
        }
    }

    private async void SaveLocalMode_OnClick(object sender, RoutedEventArgs e)
    {
        var result = await _localProvider.TestLocalConfigurationAsync(_state);
        if (!result.IsSuccess)
        {
            LocalModeStatusText.Text = result.Message;
            return;
        }

        _state.Settings.ProviderMode = "Local mode";
        _state.Settings.ProviderName = "Local";
        _state.Settings.ActiveEngine = "Local Whisper";
        Persist();
        RefreshOverview();
        RefreshLocalModeStatus();
        UpdateProviderModeStatus();
        LocalModeStatusText.Text = string.IsNullOrWhiteSpace(_state.Settings.LocalLlmModel)
            ? "Local mode saved. Dictation will use local Whisper without LLM cleanup."
            : "Local mode saved. Dictation will use local Whisper with Ollama cleanup.";
    }

    private void CopyDiagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        var diagnostics = $"""
            Trnscrbr diagnostics
            App version: {AppInfo.Version}
            Provider: {_state.Settings.ProviderName}
            Provider mode: {_state.Settings.ProviderMode}
            Active engine: {_state.Settings.ActiveEngine}
            Local Whisper executable: {RedactPath(_state.Settings.LocalWhisperExecutablePath)}
            Local Whisper model: {RedactPath(_state.Settings.LocalWhisperModelPath)}
            Local LLM endpoint: {_state.Settings.LocalLlmEndpoint}
            Local LLM model: {_state.Settings.LocalLlmModel}
            API key present: {(_credentialStore.HasOpenAiApiKey() ? "yes" : "no")}
            Microphone: {_state.Settings.MicrophoneName}
            Transcription type: {_state.Settings.CleanupMode}
            Rewrite style: {_state.Settings.RewriteStyle}
            Language mode: {_state.Settings.LanguageMode}
            English spelling: {_state.Settings.EnglishDialect}
            Paste method: {_state.Settings.PasteMethod}
            Capture startup buffer: {_state.Settings.CaptureStartupBufferMilliseconds} ms
            Contextual correction: {FormatBool(_state.Settings.ContextualCorrectionEnabled)}
            Cursor context: {FormatBool(_state.Settings.CursorContextEnabled)}
            Voice action commands: {FormatBool(_state.Settings.VoiceActionCommandsEnabled)}
            Launch on startup: {FormatBool(_state.Settings.LaunchOnStartup)}
            Floating button enabled: {FormatBool(_state.Settings.FloatingButtonEnabled)}
            Add trailing space: {FormatBool(_state.Settings.AddTrailingSpace)}
            Custom vocabulary entries: {_state.Settings.CustomVocabulary.Count}
            Hotkeys: Ctrl+Win+Space, Esc
            Transcript content: redacted
            Raw audio: redacted

            Recent log:
            {_diagnosticLog.ReadRecent()}
            """;

        System.Windows.Clipboard.SetText(diagnostics);
    }

    private void RefreshDiagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        DiagnosticsBox.Text = _diagnosticLog.ReadRecent();
    }

    private static string FormatBool(bool value)
    {
        return value ? "yes" : "no";
    }

    private void OpenDiagnosticsFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(_diagnosticLog.LogDirectory) { UseShellExecute = true });
    }

    private void RefreshUsage_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshOverview();
        UsageBox.Text = _usageStats.FormatSummary(_state.Settings.MonthlyCostWarning);
    }

    private async void CheckUpdates_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = "Checking GitHub releases...";
        var result = await _updateCheck.CheckLatestReleaseAsync();
        UpdateStatusText.Text = result.Message;
    }

    private void RefreshLocalModels()
    {
        var candidates = _localModelDiscovery.Discover();
        if (candidates.Count == 0)
        {
            LocalModelsBox.Text = "No local candidates found in common Trnscrbr, Downloads, Documents, Models, Tools, Hugging Face, or Whisper cache folders.";
            return;
        }

        LocalModelsBox.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            candidates.Select(candidate => $"{candidate.Kind}: {candidate.Name}{Environment.NewLine}{candidate.Path}"));
    }

    private void RefreshLocalModeStatus()
    {
        if (IsLocalModeConfigured())
        {
            LocalModeStatusText.Text = _state.Settings.ProviderMode == "Local mode"
                ? "Local mode is active."
                : "Local Whisper is configured. Click Use Local Mode to make it active.";
            return;
        }

        LocalModeStatusText.Text = "Choose a whisper.cpp executable and Whisper model file to enable free local dictation.";
    }

    private void MonthlyWarning_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (decimal.TryParse(MonthlyWarningBox.Text, out var warning) && warning >= 0)
        {
            _state.Settings.MonthlyCostWarning = warning;
            Persist();
            RefreshOverview();
            UsageBox.Text = _usageStats.FormatSummary(_state.Settings.MonthlyCostWarning);
        }
        else
        {
            MonthlyWarningBox.Text = _state.Settings.MonthlyCostWarning.ToString("0.00");
        }
    }

    private void ExportSettings_OnClick(object sender, RoutedEventArgs e)
    {
        Persist();

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Trnscrbr Settings",
            Filter = "Trnscrbr settings (*.trnscrbr-settings.json)|*.trnscrbr-settings.json|JSON files (*.json)|*.json",
            FileName = $"trnscrbr-settings-{DateTimeOffset.Now:yyyyMMdd}.trnscrbr-settings.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _settingsImportExport.Export(dialog.FileName, _state.Settings);
            ImportExportStatusText.Text = "Settings exported. API keys were not included.";
        }
        catch (Exception ex)
        {
            ImportExportStatusText.Text = $"Export failed: {ex.Message}";
            _diagnosticLog.Error("Settings export failed", ex);
        }
    }

    private void ImportSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Trnscrbr Settings",
            Filter = "Trnscrbr settings (*.trnscrbr-settings.json)|*.trnscrbr-settings.json|JSON files (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var imported = _settingsImportExport.Import(dialog.FileName);
            ApplyImportedSettings(imported);
            if (_state.Settings.ProviderMode == "Bring your own API key" && !_credentialStore.HasOpenAiApiKey())
            {
                _state.Settings.ProviderMode = "Not configured";
                _state.Settings.ActiveEngine = "None";
            }

            Persist();
            RefreshOverview();
            _audioCapture.ApplyPreBufferSetting();
            ImportExportStatusText.Text = "Settings imported. API keys were not imported.";
        }
        catch (Exception ex)
        {
            ImportExportStatusText.Text = $"Import failed: {ex.Message}";
            _diagnosticLog.Error("Settings import failed", ex);
        }
    }

    private void Hyperlink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Persist()
    {
        _state.Settings.CustomVocabulary = VocabularyBox.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _settingsStore.Save(_state.Settings);
        StartupService.Apply(_state.Settings);
        _state.RaiseSettingsChanged();
    }

    private void RefreshOverview()
    {
        var month = _usageStats.GetCurrentMonth();
        var threshold = _state.Settings.MonthlyCostWarning;
        var cost = month.EstimatedCostUsd;

        OverviewProviderText.Text = _state.Settings.ProviderMode;
        OverviewEngineText.Text = _state.ActiveEngineLabel;
        OverviewUsageText.Text = threshold > 0 && cost >= (double)threshold
            ? $"{month.Recordings} dictations, ${cost:0.00} of ${threshold:0.00}"
            : $"{month.Recordings} dictations, est. ${cost:0.00}";
    }

    private void UpdateProviderModeStatus()
    {
        ProviderModeStatusText.Text = _state.Settings.ProviderMode switch
        {
            "Bring your own API key" => _credentialStore.HasOpenAiApiKey()
                ? "OpenAI API key is stored. Trnscrbr can transcribe using your API account."
                : "Add an OpenAI API key on the Provider tab before dictation will work.",
            "Local mode" => IsLocalModeConfigured()
                ? "Local mode is configured. Trnscrbr will use local Whisper transcription and optional local LLM cleanup."
                : "Local mode needs a whisper.cpp executable path and model path on the Local Models tab.",
            "Cloud managed by app (planned)" => "Cloud managed by app is planned for a later paid/free-tier model and is not available yet.",
            _ => "Choose a provider now or skip and configure one later."
        };
    }

    private void UpdateApiKeyStatus()
    {
        ApiKeyStatusText.Text = _credentialStore.HasOpenAiApiKey()
            ? "OpenAI API key is stored locally in Windows Credential Manager."
            : "No OpenAI API key is stored.";
    }

    private void ApplyImportedSettings(Models.AppSettings imported)
    {
        _state.Settings.OnboardingCompleted = imported.OnboardingCompleted;
        _state.Settings.LaunchOnStartup = imported.LaunchOnStartup;
        _state.Settings.FloatingButtonEnabled = imported.FloatingButtonEnabled;
        _state.Settings.AddTrailingSpace = imported.AddTrailingSpace;
        _state.Settings.ContextualCorrectionEnabled = imported.ContextualCorrectionEnabled;
        _state.Settings.CursorContextEnabled = imported.CursorContextEnabled;
        _state.Settings.VoiceActionCommandsEnabled = imported.VoiceActionCommandsEnabled;
        _state.Settings.DiagnosticsEnabled = imported.DiagnosticsEnabled;
        _state.Settings.ForceCpuOnly = imported.ForceCpuOnly;
        _state.Settings.CaptureStartupBufferMilliseconds = imported.CaptureStartupBufferMilliseconds;
        _state.Settings.ProviderMode = imported.ProviderMode;
        _state.Settings.ProviderName = imported.ProviderName;
        _state.Settings.CleanupMode = imported.CleanupMode;
        _state.Settings.RewriteStyle = imported.RewriteStyle;
        _state.Settings.LanguageMode = imported.LanguageMode;
        _state.Settings.EnglishDialect = imported.EnglishDialect;
        _state.Settings.PasteMethod = imported.PasteMethod;
        _state.Settings.MicrophoneName = imported.MicrophoneName;
        _state.Settings.ActiveEngine = imported.ActiveEngine;
        _state.Settings.LocalWhisperExecutablePath = imported.LocalWhisperExecutablePath;
        _state.Settings.LocalWhisperModelPath = imported.LocalWhisperModelPath;
        _state.Settings.LocalLlmEndpoint = imported.LocalLlmEndpoint;
        _state.Settings.LocalLlmModel = imported.LocalLlmModel;
        _state.Settings.MonthlyCostWarning = imported.MonthlyCostWarning;
        _state.Settings.CustomVocabulary = imported.CustomVocabulary.ToList();
        VocabularyBox.Text = string.Join(Environment.NewLine, _state.Settings.CustomVocabulary);
    }

    private bool IsLocalModeConfigured()
    {
        return System.IO.File.Exists(_state.Settings.LocalWhisperExecutablePath)
            && System.IO.File.Exists(_state.Settings.LocalWhisperModelPath);
    }

    private static string RedactPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "not set" : System.IO.Path.GetFileName(path);
    }
}
