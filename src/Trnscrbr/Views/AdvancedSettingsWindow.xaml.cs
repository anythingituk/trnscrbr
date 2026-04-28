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
        UpdateApiKeyStatus();
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
        ApiKeyStatusText.Text = result.Message;
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
    }

    private void CopyDiagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        var diagnostics = $"""
            Trnscrbr diagnostics
            App version: 0.1.0
            Provider: {_state.Settings.ProviderName}
            Provider mode: {_state.Settings.ProviderMode}
            Active engine: {_state.Settings.ActiveEngine}
            API key present: {(_credentialStore.HasOpenAiApiKey() ? "yes" : "no")}
            Microphone: {_state.Settings.MicrophoneName}
            Hotkeys: Ctrl+Win+Space, Esc, Ctrl+Win+V
            Transcript content: redacted
            Raw audio: redacted

            Recent log:
            {_diagnosticLog.ReadRecent()}
            """;

        System.Windows.Clipboard.SetText(diagnostics);
    }

    private void RefreshUsage_OnClick(object sender, RoutedEventArgs e)
    {
        UsageBox.Text = _usageStats.FormatSummary(_state.Settings.MonthlyCostWarning);
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
        _state.Settings.MicrophoneName = imported.MicrophoneName;
        _state.Settings.ActiveEngine = imported.ActiveEngine;
        _state.Settings.MonthlyCostWarning = imported.MonthlyCostWarning;
        _state.Settings.CustomVocabulary = imported.CustomVocabulary.ToList();
        VocabularyBox.Text = string.Join(Environment.NewLine, _state.Settings.CustomVocabulary);
    }
}
