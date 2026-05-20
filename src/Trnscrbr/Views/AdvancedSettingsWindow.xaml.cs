using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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
    private readonly LocalHardwareProfileService _localHardwareProfile = new();
    private readonly LocalTestPhraseService _localTestPhrase;
    private readonly UpdateCheckService _updateCheck = new();
    private CancellationTokenSource? _modelDownloadCancellation;
    private UpdateCheckResult? _latestUpdateResult;
    private bool _localOperationActive;
    private bool _loadingModelPreset;
    private bool _loadingSetupAiChoice;

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
        _localTestPhrase = new LocalTestPhraseService(audioCapture, _localProvider);
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
        SelectCurrentModelPreset();
        InitializeSetupAiChoices();
        VocabularyBox.Text = string.Join(Environment.NewLine, state.Settings.CustomVocabulary);
        DiagnosticsBox.Text = _diagnosticLog.ReadRecent();
        UsageBox.Text = _usageStats.FormatSummary(_state.Settings.MonthlyCostWarning);
        CurrentVersionText.Text = $"Current version: {AppInfo.DisplayVersion}";
        OpenLatestReleaseButton.IsEnabled = false;
        RefreshOverview();
        RefreshLocalModels();
        RefreshLocalModeStatus();
        _ = RefreshHardwareGuidance();
        UpdateApiKeyStatus();
        UpdateProviderModeStatus();
        UpdateSetupPageText();
        RefreshApiKeyEntryState();
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
        UpdateSetupPageText();
    }

    private void Window_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        Persist();
        Hide();
        e.Handled = true;
    }

    private void SetupProvider_OnClick(object sender, RoutedEventArgs e)
    {
        SettingsTabControl.SelectedItem = AiModelsTab;
        ApiKeyBox.Focus();
    }

    private void SetupLocalModels_OnClick(object sender, RoutedEventArgs e)
    {
        SelectAiModelsTab();
    }

    private void SetupPrimary_OnClick(object sender, RoutedEventArgs e)
    {
        if (SetupAiChoiceComboBox.SelectedItem is not SetupAiChoiceOption option)
        {
            return;
        }

        if (option.Kind == SetupAiChoiceKind.OpenAi)
        {
            SettingsTabControl.SelectedItem = AiModelsTab;
            ShowApiKeyEntry();
            return;
        }

        SelectLocalPreset(option.LocalPresetId);
        SelectAiModelsTab();
        DownloadModel_OnClick(sender, e);
    }

    private void SetupAiChoice_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingSetupAiChoice || SetupAiChoiceComboBox.SelectedItem is not SetupAiChoiceOption option)
        {
            return;
        }

        ApplyAiChoice(option);
    }

    private void AiModelsChoice_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingSetupAiChoice || AiModelsChoiceComboBox.SelectedItem is not SetupAiChoiceOption option)
        {
            return;
        }

        ApplyAiChoice(option);
    }

    public void SelectAiModelsTab()
    {
        SettingsTabControl.SelectedItem = AiModelsTab;
        LocalModeStatusText.Text = IsLocalModeConfigured()
            ? "Local AI is configured."
            : "Download the selected local model to enable local dictation.";
    }

    private bool BeginLocalOperation(string operationName)
    {
        if (_localOperationActive)
        {
            LocalModeStatusText.Text = "Another local setup operation is already running.";
            return false;
        }

        _localOperationActive = true;
        SetLocalOperationControlsEnabled(false);
        LocalModeStatusText.Text = operationName;
        return true;
    }

    private void EndLocalOperation()
    {
        _localOperationActive = false;
        SetLocalOperationControlsEnabled(true);
    }

    private void SetLocalTestStatus(string message)
    {
        LocalTestStatusText.Text = message;
        LocalModeStatusText.Text = message;
    }

    private void SetLocalOperationControlsEnabled(bool enabled)
    {
        DownloadModelButton.IsEnabled = enabled;
        VerifyModelButton.IsEnabled = enabled;
        RemoveModelButton.IsEnabled = enabled;
        InstallWhisperCliButton.IsEnabled = enabled;
        CheckWhisperCliUpdateButton.IsEnabled = enabled;
        CancelModelDownloadButton.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        BrowseWhisperExecutableButton.IsEnabled = enabled;
        BrowseWhisperModelButton.IsEnabled = enabled;
        TestLocalSetupButton.IsEnabled = enabled;
        RunSmokeTestButton.IsEnabled = enabled;
        RecordTestPhraseButton.IsEnabled = enabled;
        DetectLocalModelsButton.IsEnabled = enabled;
        DetectOllamaModelsButton.IsEnabled = enabled;
        ModelPresetComboBox.IsEnabled = enabled;
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
        RefreshApiKeyEntryState();
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
        RefreshApiKeyEntryState();
    }

    private void ShowApiKeyEntry_OnClick(object sender, RoutedEventArgs e)
    {
        ShowApiKeyEntry();
    }

    private void ShowApiKeyEntry()
    {
        ApiKeyEntryPanel.Visibility = Visibility.Visible;
        SaveKeyButton.Visibility = Visibility.Visible;
        ApiKeyBox.Focus();
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
        var hotkeyChanged = sender == ToggleHotkeyComboBox
            || sender == PushToTalkHotkeyComboBox
            || sender == SetupToggleHotkeyComboBox;
        if (_state.Settings.ProviderMode == "Local mode")
        {
            _state.Settings.ProviderName = "Local";
            _state.Settings.ActiveEngine = "Local AI";
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
        if (hotkeyChanged)
        {
            ValidateHotkeySettings();
        }

        RefreshOverview();
        UpdateProviderModeStatus();
        UpdateSetupPageText();
        RefreshLocalModeStatus();
    }

    private void ValidateHotkeySettings()
    {
        if (string.Equals(_state.Settings.ToggleRecordingHotkey, _state.Settings.PushToTalkHotkey, StringComparison.OrdinalIgnoreCase))
        {
            _state.Settings.PushToTalkHotkey = _state.Settings.ToggleRecordingHotkey == "Ctrl+Alt+Space"
                ? "F10"
                : "Ctrl+Alt+Space";
            Persist();
            _state.RaiseSettingsChanged();
            HotkeyStatusText.Text = "Toggle and push-to-talk shortcuts must be different. Push-to-talk was moved to a non-conflicting shortcut.";
            return;
        }

        HotkeyStatusText.Text = $"Hotkeys updated. Toggle: {FormatHotkey(_state.Settings.ToggleRecordingHotkey)}. Push-to-talk: {FormatHotkey(_state.Settings.PushToTalkHotkey)}.";
    }

    private static string FormatHotkey(string hotkey)
    {
        return hotkey.Replace("+", " + ", StringComparison.Ordinal);
    }

    private void InitializeSetupAiChoices()
    {
        _loadingSetupAiChoice = true;
        try
        {
            var options = LocalModelDownloadService.Presets
                .Select(preset => new SetupAiChoiceOption(
                    SetupAiChoiceKind.Local,
                    preset.Id,
                    $"Local AI - {preset.DisplayName}"))
                .Append(new SetupAiChoiceOption(
                    SetupAiChoiceKind.OpenAi,
                    string.Empty,
                    "OpenAI API"))
                .ToList();

            SetupAiChoiceComboBox.ItemsSource = options;
            AiModelsChoiceComboBox.ItemsSource = options;
            SelectSetupAiChoice();
        }
        finally
        {
            _loadingSetupAiChoice = false;
        }
    }

    private void SelectSetupAiChoice()
    {
        if (SetupAiChoiceComboBox.ItemsSource is not IEnumerable<SetupAiChoiceOption> options)
        {
            return;
        }

        var wasLoading = _loadingSetupAiChoice;
        _loadingSetupAiChoice = true;
        try
        {
            SetupAiChoiceOption? selected;
            if (string.Equals(_state.Settings.ProviderMode, "Bring your own API key", StringComparison.OrdinalIgnoreCase))
            {
                selected = options.FirstOrDefault(option => option.Kind == SetupAiChoiceKind.OpenAi);
            }
            else
            {
                var presetId = string.IsNullOrWhiteSpace(_state.Settings.LocalWhisperModelPresetId)
                    ? "small"
                    : _state.Settings.LocalWhisperModelPresetId;
                selected = options.FirstOrDefault(option =>
                    option.Kind == SetupAiChoiceKind.Local
                    && string.Equals(option.LocalPresetId, presetId, StringComparison.OrdinalIgnoreCase));
            }

            var choice = selected ?? options.FirstOrDefault();
            SetupAiChoiceComboBox.SelectedItem = choice;
            AiModelsChoiceComboBox.SelectedItem = choice;
        }
        finally
        {
            _loadingSetupAiChoice = wasLoading;
        }
    }

    private void ApplyAiChoice(SetupAiChoiceOption option)
    {
        if (option.Kind == SetupAiChoiceKind.OpenAi)
        {
            _state.Settings.ProviderMode = "Bring your own API key";
            _state.Settings.ProviderName = "OpenAI";
            _state.Settings.ActiveEngine = "OpenAI";
        }
        else
        {
            _state.Settings.ProviderMode = "Local mode";
            _state.Settings.ProviderName = "Local";
            _state.Settings.ActiveEngine = "Local AI";
            _state.Settings.LocalWhisperModelPresetId = option.LocalPresetId;
            var preset = LocalModelDownloadService.Presets.FirstOrDefault(candidate => candidate.Id == option.LocalPresetId);
            if (preset is not null)
            {
                var modelPath = GetManagedModelPath(preset);
                _state.Settings.LocalWhisperModelPath = File.Exists(modelPath) ? modelPath : string.Empty;
            }

            SelectLocalPreset(option.LocalPresetId);
        }

        Persist();
        RefreshOverview();
        RefreshLocalModeStatus();
        UpdateProviderModeStatus();
        UpdateSetupPageText();
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
        LocalTestStatusText.Text = "Local model detection refreshed.";
    }

    private void ModelPreset_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingModelPreset)
        {
            return;
        }

        if (ModelPresetComboBox.SelectedItem is not LocalModelPreset preset)
        {
            ModelPresetDescriptionText.Text = string.Empty;
            return;
        }

        ModelPresetDescriptionText.Text = FormatModelPresetGuidance(preset);
        _state.Settings.LocalWhisperModelPresetId = preset.Id;
        var modelPath = GetManagedModelPath(preset);
        _state.Settings.LocalWhisperModelPath = File.Exists(modelPath) ? modelPath : string.Empty;
        _state.Settings.ProviderMode = "Local mode";
        _state.Settings.ProviderName = "Local";
        _state.Settings.ActiveEngine = "Local AI";
        Persist();
        RefreshOverview();
        RefreshLocalModeStatus();
        UpdateProviderModeStatus();
        UpdateSetupPageText();
    }

    private void SelectCurrentModelPreset()
    {
        _loadingModelPreset = true;
        try
        {
            var selected = _localModelDownload.FindPreset(
                    _state.Settings.LocalWhisperModelPath,
                    _state.Settings.LocalWhisperModelPresetId)
                ?? LocalModelDownloadService.Presets.First(candidate => candidate.Id == "small");
            ModelPresetComboBox.SelectedItem = selected;
            ModelPresetDescriptionText.Text = FormatModelPresetGuidance(selected);
        }
        finally
        {
            _loadingModelPreset = false;
        }
    }

    private void SelectLocalPreset(string presetId)
    {
        _loadingModelPreset = true;
        try
        {
            var selected = LocalModelDownloadService.Presets.FirstOrDefault(candidate => candidate.Id == presetId)
                ?? LocalModelDownloadService.Presets.First(candidate => candidate.Id == "small");
            ModelPresetComboBox.SelectedItem = selected;
            ModelPresetDescriptionText.Text = FormatModelPresetGuidance(selected);
            _state.Settings.LocalWhisperModelPresetId = selected.Id;
        }
        finally
        {
            _loadingModelPreset = false;
        }
    }

    private static string FormatModelPresetGuidance(LocalModelPreset preset)
    {
        var recommendation = preset.Id switch
        {
            "small" => "Recommended for most users.",
            "medium" => "May feel slow on many PCs. Use Small if you want faster dictation.",
            "large" => "Can be very slow on CPU-only local AI. Use only when quality matters more than speed.",
            _ => string.Empty
        };

        return $"{recommendation} {preset.Description} Download: {preset.DiskSize}. Recommended memory: {preset.RamRecommendation}.";
    }

    private async void DownloadModel_OnClick(object sender, RoutedEventArgs e)
    {
        if (!BeginLocalOperation("Preparing local AI download..."))
        {
            return;
        }

        if (ModelPresetComboBox.SelectedItem is not LocalModelPreset preset)
        {
            LocalModeStatusText.Text = "Choose a model preset first.";
            EndLocalOperation();
            return;
        }

        _modelDownloadCancellation?.Cancel();
        _modelDownloadCancellation?.Dispose();
        _modelDownloadCancellation = new CancellationTokenSource();

        try
        {
            LocalModeStatusText.Text = "Checking local engine...";
            var cli = await _localWhisperToolDownload.TryUseExistingLatestX64Async(_modelDownloadCancellation.Token);
            if (cli is null)
            {
                LocalModeStatusText.Text = "Downloading local engine...";
                cli = await _localWhisperToolDownload.DownloadLatestX64Async(
                    new Progress<double>(value => LocalModeStatusText.Text = $"Downloading local engine: {value:P0}"),
                    _modelDownloadCancellation.Token);
            }

            LocalModeStatusText.Text = $"Checking {preset.DisplayName}...";
            var model = await _localModelDownload.TryUseExistingAsync(preset, _modelDownloadCancellation.Token);
            if (model is null)
            {
                LocalModeStatusText.Text = $"Downloading {preset.DisplayName}...";
                model = await _localModelDownload.DownloadAsync(
                    preset,
                    new Progress<double>(value => LocalModeStatusText.Text = $"Downloading {preset.DisplayName}: {value:P0}"),
                    _modelDownloadCancellation.Token);
            }

            _state.Settings.LocalWhisperExecutablePath = cli.ExecutablePath;
            _state.Settings.LocalWhisperCliVersion = cli.Version;
            _state.Settings.LocalWhisperModelPath = model.ModelPath;
            _state.Settings.LocalWhisperModelPresetId = model.PresetId;
            _state.Settings.LocalSetupSource = "AI Models download";
            _state.Settings.LocalSetupCompletedAt = DateTimeOffset.Now;
            _state.Settings.ProviderMode = "Local mode";
            _state.Settings.ProviderName = "Local";
            _state.Settings.ActiveEngine = "Local AI";
            Persist();
            RefreshOverview();
            RefreshLocalModels();
            RefreshLocalModeStatus();
            UpdateProviderModeStatus();
            UpdateSetupPageText();
            LocalModeStatusText.Text = $"{preset.DisplayName} already downloaded and ready.";
        }
        catch (OperationCanceledException)
        {
            LocalModeStatusText.Text = "Download cancelled. Partial downloads were kept so they can resume later.";
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            LocalModeStatusText.Text = LocalSetupErrorFormatter.Format("Local AI download failed", ex);
            _diagnosticLog.Error("Local AI download failed", ex, new Dictionary<string, string>
            {
                ["preset"] = preset.Id,
                ["fileName"] = preset.FileName
            });
        }
        finally
        {
            EndLocalOperation();
        }
    }

    private void CancelModelDownload_OnClick(object sender, RoutedEventArgs e)
    {
        _modelDownloadCancellation?.Cancel();
    }

    private async void VerifyModel_OnClick(object sender, RoutedEventArgs e)
    {
        if (!BeginLocalOperation("Verifying local AI model..."))
        {
            return;
        }

        try
        {
            var modelPath = _state.Settings.LocalWhisperModelPath;
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                LocalModeStatusText.Text = "No configured local model file was found.";
                return;
            }

            var preset = _localModelDownload.FindPreset(modelPath, _state.Settings.LocalWhisperModelPresetId);
            if (preset is null)
            {
                LocalModeStatusText.Text = "This model is custom, so checksum verification is unavailable.";
                return;
            }

            var verified = await _localModelDownload.VerifyPresetAsync(modelPath, preset);
            if (verified)
            {
                _state.Settings.LocalWhisperModelPresetId = preset.Id;
                Persist();
                LocalModeStatusText.Text = $"Model verified: {preset.DisplayName}.";
                return;
            }

            LocalModeStatusText.Text = $"Model failed verification: {preset.DisplayName}. Click Download Model to download it again.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            LocalModeStatusText.Text = LocalSetupErrorFormatter.Format("Model verification failed", ex);
            _diagnosticLog.Error("Local model verification failed", ex);
        }
        finally
        {
            EndLocalOperation();
        }
    }

    private void OpenModelsFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_localModelDownload.ModelsDirectory);
        Process.Start(new ProcessStartInfo(_localModelDownload.ModelsDirectory) { UseShellExecute = true });
    }

    private async void InstallWhisperCli_OnClick(object sender, RoutedEventArgs e)
    {
        if (!BeginLocalOperation("Downloading local engine..."))
        {
            return;
        }

        LocalModeStatusText.Text = "Downloading local engine...";
        var progress = new Progress<double>(value =>
        {
            LocalModeStatusText.Text = $"Downloading local engine: {value:P0}";
        });

        try
        {
            var cli = await _localWhisperToolDownload.DownloadLatestX64Async(progress);
            _state.Settings.LocalWhisperExecutablePath = cli.ExecutablePath;
            _state.Settings.LocalWhisperCliVersion = cli.Version;
            _state.Settings.LocalSetupSource = "Manual local engine install";
            _state.Settings.LocalSetupCompletedAt = DateTimeOffset.Now;
            Persist();
            RefreshOverview();
            RefreshLocalModels();
            RefreshLocalModeStatus();
            UpdateProviderModeStatus();
            LocalModeStatusText.Text = "Local engine installed and verified.";
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            LocalModeStatusText.Text = LocalSetupErrorFormatter.Format("Could not install local engine", ex);
            _diagnosticLog.Error("Whisper CLI install failed", ex);
        }
        finally
        {
            EndLocalOperation();
        }
    }

    private async void CheckWhisperCliUpdate_OnClick(object sender, RoutedEventArgs e)
    {
        if (!BeginLocalOperation("Checking local engine update..."))
        {
            return;
        }

        try
        {
            var result = await _localWhisperToolDownload.CheckLatestX64Async(_state.Settings.LocalWhisperCliVersion);
            LocalModeStatusText.Text = result.Message;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            LocalModeStatusText.Text = LocalSetupErrorFormatter.Format("Could not check local engine update", ex);
            _diagnosticLog.Error("Whisper CLI update check failed", ex);
        }
        finally
        {
            EndLocalOperation();
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
            LocalModeStatusText.Text = LocalSetupErrorFormatter.Format("Could not remove model", ex);
            _diagnosticLog.Error("Local model removal failed", ex);
        }
    }

    private async void DetectOllamaModels_OnClick(object sender, RoutedEventArgs e)
    {
        if (!BeginLocalOperation("Checking Ollama models..."))
        {
            return;
        }

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
            LocalModeStatusText.Text = LocalSetupErrorFormatter.Format($"Could not reach Ollama at {_state.Settings.LocalLlmEndpoint}", ex);
        }
        finally
        {
            EndLocalOperation();
        }
    }

    private void BrowseWhisperExecutable_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose local engine executable",
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
            Title = "Choose local AI model",
            Filter = "Local AI model files (*.bin;*.gguf)|*.bin;*.gguf|All files (*.*)|*.*"
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
        if (!BeginLocalOperation("Testing local setup..."))
        {
            return;
        }

        SetLocalTestStatus("Testing local setup...");
        try
        {
            var result = await _localProvider.TestLocalConfigurationAsync(_state);
            SetLocalTestStatus(result.Message);
        }
        finally
        {
            EndLocalOperation();
        }
    }

    private async void RunLocalSmokeTest_OnClick(object sender, RoutedEventArgs e)
    {
        if (!BeginLocalOperation("Running local AI test..."))
        {
            return;
        }

        SetLocalTestStatus("Running local AI test...");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            var result = await _localProvider.RunWhisperSmokeTestAsync(_state, timeout.Token);
            SetLocalTestStatus(result.Message);
        }
        catch (OperationCanceledException)
        {
            SetLocalTestStatus("Local AI test timed out after 2 minutes.");
        }
        finally
        {
            EndLocalOperation();
        }
    }

    private async void RecordLocalTestPhrase_OnClick(object sender, RoutedEventArgs e)
    {
        if (!BeginLocalOperation("Preparing local test phrase..."))
        {
            return;
        }

        if (_state.RecordingState is RecordingState.Recording or RecordingState.Processing)
        {
            SetLocalTestStatus("Finish the current recording before running a local test phrase.");
            EndLocalOperation();
            return;
        }

        var setup = await _localProvider.TestLocalConfigurationAsync(_state);
        if (!setup.IsSuccess)
        {
            SetLocalTestStatus(setup.Message);
            EndLocalOperation();
            return;
        }

        LocalTestTranscriptBox.Text = string.Empty;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            var result = await _localTestPhrase.RunAsync(_state, SetLocalTestStatus, timeout.Token);
            LocalTestTranscriptBox.Text = result.Transcript;
            SetLocalTestStatus(result.NoInputCaptured
                ? $"{result.Message} Check the selected microphone."
                : result.Message);
        }
        catch (OperationCanceledException)
        {
            SetLocalTestStatus("Local test phrase timed out.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            SetLocalTestStatus(LocalSetupErrorFormatter.Format("Local test phrase failed", ex));
            _diagnosticLog.Error("Local test phrase failed", ex);
        }
        finally
        {
            _state.RecordingState = RecordingState.Idle;
            _state.StatusMessage = "Ready";
            EndLocalOperation();
        }
    }

    private void CopyDiagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        var diagnostics = $"""
            Trnscrbr diagnostics
            App version: {AppInfo.Version}
            OS: {RuntimeInformation.OSDescription}
            Framework: {RuntimeInformation.FrameworkDescription}
            Process architecture: {RuntimeInformation.ProcessArchitecture}
            Provider: {_state.Settings.ProviderName}
            Provider mode: {_state.Settings.ProviderMode}
            Active engine: {_state.Settings.ActiveEngine}
            Local Whisper executable: {RedactPath(_state.Settings.LocalWhisperExecutablePath)}
            Local Whisper model: {RedactPath(_state.Settings.LocalWhisperModelPath)}
            Local Whisper CLI version: {FormatOptional(_state.Settings.LocalWhisperCliVersion)}
            Local model preset: {FormatOptional(_state.Settings.LocalWhisperModelPresetId)}
            Local setup source: {FormatOptional(_state.Settings.LocalSetupSource)}
            Local setup completed: {_state.Settings.LocalSetupCompletedAt?.ToString("u") ?? "not set"}
            Force CPU-only local mode: {FormatBool(_state.Settings.ForceCpuOnly)}
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
            Ignore other speakers: {FormatBool(_state.Settings.IgnoreOtherSpeakersEnabled)}
            Voice action commands: {FormatBool(_state.Settings.VoiceActionCommandsEnabled)}
            Launch on startup: {FormatBool(_state.Settings.LaunchOnStartup)}
            Floating button enabled: {FormatBool(_state.Settings.FloatingButtonEnabled)}
            Add trailing space: {FormatBool(_state.Settings.AddTrailingSpace)}
            Custom vocabulary entries: {_state.Settings.CustomVocabulary.Count}
            Global hotkeys enabled: {FormatBool(_state.Settings.GlobalHotkeysEnabled)}
            Hotkeys: toggle {_state.Settings.ToggleRecordingHotkey}, push-to-talk {_state.Settings.PushToTalkHotkey}, Esc
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

    private static string FormatOptional(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "not set" : value;
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

    private async void RefreshHardwareGuidance_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshHardwareGuidance();
    }

    private async Task RefreshHardwareGuidance()
    {
        LocalHardwareGuidanceText.Text = "Checking this PC for local AI guidance...";
        var profile = await _localHardwareProfile.DetectAsync();
        LocalHardwareGuidanceText.Text = profile.Guidance;
    }

    private async void CheckUpdates_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = "Checking GitHub releases...";
        var result = await _updateCheck.CheckLatestReleaseAsync();
        _latestUpdateResult = result;
        UpdateStatusText.Text = result.Message;
        OpenLatestReleaseButton.IsEnabled = !string.IsNullOrWhiteSpace(result.ReleaseUrl);
    }

    private void OpenLatestRelease_OnClick(object sender, RoutedEventArgs e)
    {
        var url = string.IsNullOrWhiteSpace(_latestUpdateResult?.ReleaseUrl)
            ? AppInfo.ReleasesUrl
            : _latestUpdateResult.ReleaseUrl;

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void RefreshLocalModels()
    {
        var candidates = _localModelDiscovery.Discover();
        if (candidates.Count == 0)
        {
            LocalModelsBox.Text = "No local AI files found in common Trnscrbr, Downloads, Documents, Models, Tools, or cache folders.";
            return;
        }

        LocalModelsBox.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            candidates.Select(candidate => $"{candidate.Kind}: {candidate.Name}{Environment.NewLine}{candidate.Path}"));
    }

    private void RefreshLocalModeStatus()
    {
        RefreshLocalInstallSummary();

        if (IsLocalModeConfigured())
        {
            LocalModeStatusText.Text = "Local AI is ready.";
            return;
        }

        LocalModeStatusText.Text = "Download the selected local model to enable local dictation.";
    }

    private void RefreshLocalInstallSummary()
    {
        var cliPath = _state.Settings.LocalWhisperExecutablePath;
        var modelPath = _state.Settings.LocalWhisperModelPath;
        var modelPreset = _localModelDownload.FindPreset(modelPath, _state.Settings.LocalWhisperModelPresetId);
        var hasCli = File.Exists(cliPath);
        var hasModel = File.Exists(modelPath);

        LocalCliSummaryText.Text = hasCli
            ? $"{FormatOptional(_state.Settings.LocalWhisperCliVersion)} - installed"
            : "Not installed";
        LocalModelSummaryText.Text = hasModel
            ? $"{modelPreset?.DisplayName ?? "Custom model"} - {RedactPath(modelPath)}"
            : "Not installed";
        LocalSetupSummaryText.Text = _state.Settings.LocalSetupCompletedAt is { } completedAt
            ? $"{FormatOptional(_state.Settings.LocalSetupSource)} - {completedAt:g}"
            : "Not completed";

        RefreshSelectedLocalModelState();
    }

    private void RefreshSelectedLocalModelState()
    {
        if (ModelPresetComboBox.SelectedItem is not LocalModelPreset preset)
        {
            SelectedModelStatusText.Text = "Choose a local model.";
            DownloadModelButton.Visibility = Visibility.Collapsed;
            return;
        }

        var modelPath = GetManagedModelPath(preset);
        var modelDownloaded = File.Exists(modelPath);
        var cliDownloaded = File.Exists(_state.Settings.LocalWhisperExecutablePath);
        if (modelDownloaded && cliDownloaded)
        {
            SelectedModelStatusText.Text = $"{preset.DisplayName} already downloaded.";
            DownloadModelButton.Visibility = Visibility.Collapsed;
            return;
        }

        SelectedModelStatusText.Text = modelDownloaded
            ? $"{preset.DisplayName} already downloaded. Local engine still needs to be installed."
            : $"{preset.DisplayName} needs to be downloaded.";
        DownloadModelButton.Content = modelDownloaded ? "Install Engine" : "Download";
        DownloadModelButton.Visibility = Visibility.Visible;
    }

    private string GetManagedModelPath(LocalModelPreset preset)
    {
        return Path.Combine(_localModelDownload.ModelsDirectory, preset.FileName);
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
        OverviewEngineText.Text = _state.Settings.ProviderMode == "Local mode" && !string.IsNullOrWhiteSpace(_state.Settings.LocalWhisperCliVersion)
            ? _state.ActiveEngineLabel
            : _state.ActiveEngineLabel;
        OverviewUsageText.Text = threshold > 0 && cost >= (double)threshold
            ? $"{month.Recordings} dictations, ${cost:0.00} of ${threshold:0.00}"
            : $"{month.Recordings} dictations, est. ${cost:0.00}";
        UpdateSetupPageText();
    }

    private void UpdateProviderModeStatus()
    {
        var selectedPreset = ModelPresetComboBox.SelectedItem as LocalModelPreset
            ?? LocalModelDownloadService.Presets.First(candidate => candidate.Id == "small");

        var status = _state.Settings.ProviderMode switch
        {
            "Bring your own API key" => _credentialStore.HasOpenAiApiKey()
                ? "OpenAI is ready."
                : "OpenAI needs your API key.",
            "Local mode" => IsLocalModeConfigured()
                ? $"Local AI is ready with {selectedPreset.DisplayName}."
                : $"{selectedPreset.DisplayName} needs to be downloaded before local dictation is ready.",
            "Cloud managed by app (planned)" => "Cloud managed by app is planned for a later paid/free-tier model and is not available yet.",
            _ => "Choose local AI for free use, or OpenAI with your own API key."
        };

        ProviderModeStatusText.Text = status;
        AiModelsChoiceStatusText.Text = status;
    }

    private void UpdateSetupPageText()
    {
        SelectSetupAiChoice();
        var openAiNeedsKey = string.Equals(_state.Settings.ProviderMode, "Bring your own API key", StringComparison.OrdinalIgnoreCase)
            && !_credentialStore.HasOpenAiApiKey();

        if (_state.IsProviderConfigured && !openAiNeedsKey)
        {
            SetupTitleText.Text = "Setup";
            SetupIntroText.Text = "Choose the dictation engine and the shortcut you want to use.";
            SetupPrimaryButton.Visibility = Visibility.Collapsed;
            SetupSkipButton.Content = "Done";
            return;
        }

        SetupTitleText.Text = "First setup";
        SetupIntroText.Text = "Small local AI is selected by default. Download it now, or choose OpenAI, Medium, or Large.";
        SetupSkipButton.Visibility = Visibility.Visible;
        SetupSkipButton.Content = "Decide later";

        if (string.Equals(_state.Settings.ProviderMode, "Bring your own API key", StringComparison.OrdinalIgnoreCase))
        {
            SetupPrimaryButton.Content = _credentialStore.HasOpenAiApiKey() ? "OpenAI Ready" : "Add OpenAI API Key";
            SetupPrimaryButton.Visibility = _credentialStore.HasOpenAiApiKey() ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        var preset = ModelPresetComboBox.SelectedItem as LocalModelPreset
            ?? LocalModelDownloadService.Presets.First(candidate => candidate.Id == "small");
        var selectedReady = IsSelectedLocalModelReady();
        SetupPrimaryButton.Content = selectedReady
            ? "Local AI Ready"
            : $"Download {preset.DisplayName.Split(" - ")[0]} Local AI";
        SetupPrimaryButton.Visibility = selectedReady ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateApiKeyStatus()
    {
        var hasKey = _credentialStore.HasOpenAiApiKey();
        ApiKeyStatusText.Text = hasKey
            ? "OpenAI key is saved locally in Windows Credential Manager."
            : "No OpenAI key is saved.";
        RefreshApiKeyEntryState();
    }

    private void RefreshApiKeyEntryState()
    {
        var hasKey = _credentialStore.HasOpenAiApiKey();
        ApiKeySavedPanel.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;
        DeleteKeyButton.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyEntryPanel.Visibility = hasKey ? Visibility.Collapsed : Visibility.Visible;
        SaveKeyButton.Visibility = hasKey ? Visibility.Collapsed : Visibility.Visible;
        ShowApiKeyEntryButton.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;
        ShowApiKeyEntryButton.Content = hasKey ? "Replace Key" : "Add Key";
    }

    private void ApplyImportedSettings(Models.AppSettings imported)
    {
        _state.Settings.OnboardingCompleted = imported.OnboardingCompleted;
        _state.Settings.LaunchOnStartup = imported.LaunchOnStartup;
        _state.Settings.FloatingButtonEnabled = imported.FloatingButtonEnabled;
        _state.Settings.AddTrailingSpace = imported.AddTrailingSpace;
        _state.Settings.ContextualCorrectionEnabled = imported.ContextualCorrectionEnabled;
        _state.Settings.CursorContextEnabled = imported.CursorContextEnabled;
        _state.Settings.IgnoreOtherSpeakersEnabled = imported.IgnoreOtherSpeakersEnabled;
        _state.Settings.VoiceActionCommandsEnabled = imported.VoiceActionCommandsEnabled;
        _state.Settings.DiagnosticsEnabled = imported.DiagnosticsEnabled;
        _state.Settings.ForceCpuOnly = imported.ForceCpuOnly;
        _state.Settings.AutoLanguageHintApplied = imported.AutoLanguageHintApplied;
        _state.Settings.CaptureStartupBufferMilliseconds = imported.CaptureStartupBufferMilliseconds;
        _state.Settings.AutoEnglishDictationCount = imported.AutoEnglishDictationCount;
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
        _state.Settings.LocalWhisperCliVersion = imported.LocalWhisperCliVersion;
        _state.Settings.LocalWhisperModelPresetId = imported.LocalWhisperModelPresetId;
        _state.Settings.LocalSetupSource = imported.LocalSetupSource;
        _state.Settings.LocalSetupCompletedAt = imported.LocalSetupCompletedAt;
        _state.Settings.MonthlyCostWarning = imported.MonthlyCostWarning;
        _state.Settings.CustomVocabulary = imported.CustomVocabulary.ToList();
        VocabularyBox.Text = string.Join(Environment.NewLine, _state.Settings.CustomVocabulary);
    }

    private bool IsLocalModeConfigured()
    {
        return System.IO.File.Exists(_state.Settings.LocalWhisperExecutablePath)
            && System.IO.File.Exists(_state.Settings.LocalWhisperModelPath);
    }

    private bool IsSelectedLocalModelReady()
    {
        if (ModelPresetComboBox.SelectedItem is not LocalModelPreset preset)
        {
            return IsLocalModeConfigured();
        }

        return File.Exists(_state.Settings.LocalWhisperExecutablePath)
            && File.Exists(GetManagedModelPath(preset));
    }

    private static string RedactPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "not set" : System.IO.Path.GetFileName(path);
    }

    private enum SetupAiChoiceKind
    {
        Local,
        OpenAi
    }

    private sealed record SetupAiChoiceOption(
        SetupAiChoiceKind Kind,
        string LocalPresetId,
        string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
