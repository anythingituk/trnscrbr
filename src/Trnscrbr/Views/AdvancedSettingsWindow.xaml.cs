using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Win32;
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
        VocabularyBox.Text = string.Join(Environment.NewLine, state.Settings.CustomVocabulary);
        DiagnosticsBox.Text = _diagnosticLog.ReadRecent();
        UsageBox.Text = _usageStats.FormatSummary(_state.Settings.MonthlyCostWarning);
        CurrentVersionText.Text = $"Current version: {AppInfo.Version}";
        RefreshLocalModels();
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
        _state.Settings.ActiveEngine = "OpenAI";
        Persist();
        UpdateProviderModeStatus();
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
        UpdateApiKeyStatus();
        UpdateProviderModeStatus();
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
        Persist();
        UpdateProviderModeStatus();
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

    private void CopyDiagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        var diagnostics = $"""
            Trnscrbr diagnostics
            App version: {AppInfo.Version}
            Provider: {_state.Settings.ProviderName}
            Provider mode: {_state.Settings.ProviderMode}
            Active engine: {_state.Settings.ActiveEngine}
            API key present: {(_credentialStore.HasOpenAiApiKey() ? "yes" : "no")}
            Microphone: {_state.Settings.MicrophoneName}
            Cleanup mode: {_state.Settings.CleanupMode}
            Language mode: {_state.Settings.LanguageMode}
            Paste method: {_state.Settings.PasteMethod}
            Capture startup buffer: {_state.Settings.CaptureStartupBufferMilliseconds} ms
            Contextual correction: {FormatBool(_state.Settings.ContextualCorrectionEnabled)}
            Cursor context: {FormatBool(_state.Settings.CursorContextEnabled)}
            Voice action commands: {FormatBool(_state.Settings.VoiceActionCommandsEnabled)}
            Launch on startup: {FormatBool(_state.Settings.LaunchOnStartup)}
            Floating button enabled: {FormatBool(_state.Settings.FloatingButtonEnabled)}
            Add trailing space: {FormatBool(_state.Settings.AddTrailingSpace)}
            Custom vocabulary entries: {_state.Settings.CustomVocabulary.Count}
            Hotkeys: Ctrl+Win+Space, Esc, Ctrl+Win+V
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
        UsageBox.Text = _usageStats.FormatSummary(_state.Settings.MonthlyCostWarning);
    }

    private void RefreshLocalModels()
    {
        var candidates = _localModelDiscovery.Discover();
        if (candidates.Count == 0)
        {
            LocalModelsBox.Text = "No local model candidates found in Trnscrbr, Hugging Face, or Whisper cache folders.";
            return;
        }

        LocalModelsBox.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            candidates.Select(candidate => $"{candidate.Name}{Environment.NewLine}{candidate.Path}"));
    }

    private void MonthlyWarning_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (decimal.TryParse(MonthlyWarningBox.Text, out var warning) && warning >= 0)
        {
            _state.Settings.MonthlyCostWarning = warning;
            Persist();
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

    private void UpdateProviderModeStatus()
    {
        ProviderModeStatusText.Text = _state.Settings.ProviderMode switch
        {
            "Bring your own API key" => _credentialStore.HasOpenAiApiKey()
                ? "OpenAI API key is stored. Trnscrbr can transcribe using your API account."
                : "Add an OpenAI API key on the Provider tab before dictation will work.",
            "Local mode" => "Local mode is planned for this MVP track. Detected models are shown on the Local Models tab, but they are not used automatically.",
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
        _state.Settings.LanguageMode = imported.LanguageMode;
        _state.Settings.PasteMethod = imported.PasteMethod;
        _state.Settings.MicrophoneName = imported.MicrophoneName;
        _state.Settings.ActiveEngine = imported.ActiveEngine;
        _state.Settings.MonthlyCostWarning = imported.MonthlyCostWarning;
        _state.Settings.CustomVocabulary = imported.CustomVocabulary.ToList();
        VocabularyBox.Text = string.Join(Environment.NewLine, _state.Settings.CustomVocabulary);
    }
}
