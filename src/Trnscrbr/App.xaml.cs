using System.Windows;
using Trnscrbr.Services;
using Trnscrbr.ViewModels;
using Trnscrbr.Views;

namespace Trnscrbr;

public partial class App : System.Windows.Application
{
    private AppSettingsStore? _settingsStore;
    private KeyboardHookService? _keyboardHook;
    private TrayIconService? _trayIcon;
    private AudioCaptureService? _audioCapture;
    private CredentialStore? _credentialStore;
    private OpenAiProviderService? _openAiProvider;
    private DiagnosticLogService? _diagnosticLog;
    private UsageStatsService? _usageStats;
    private RecordingCoordinator? _recording;
    private FloatingButtonWindow? _floatingButton;
    private TrayPanelWindow? _trayPanel;
    private AdvancedSettingsWindow? _advancedSettings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsStore = new AppSettingsStore();
        _credentialStore = new CredentialStore();
        _openAiProvider = new OpenAiProviderService();
        _diagnosticLog = new DiagnosticLogService();
        _usageStats = new UsageStatsService();
        var settings = _settingsStore.Load();
        var appState = new AppStateViewModel(settings);

        _floatingButton = new FloatingButtonWindow(appState);
        _audioCapture = new AudioCaptureService(appState);
        var insertion = new TextInsertionService(appState, _diagnosticLog);
        _recording = new RecordingCoordinator(appState, insertion, _floatingButton, _audioCapture, _credentialStore, _openAiProvider, _diagnosticLog, _usageStats);
        _floatingButton.ToggleRecordingRequested += (_, _) => _recording.ToggleRecording();
        _floatingButton.SettingsRequested += (_, _) => ShowTrayPanel();
        _floatingButton.QuitRequested += (_, _) => Shutdown();

        _keyboardHook = new KeyboardHookService();
        _keyboardHook.PushToTalkPressed += (_, _) => _recording.HandlePushToTalkPressed();
        _keyboardHook.PushToTalkReleased += (_, _) => _recording.HandlePushToTalkReleased();
        _keyboardHook.CancelPressed += (_, _) => _recording.Cancel();
        _keyboardHook.PasteLastTranscriptPressed += (_, _) => _recording.PasteLastTranscript();
        _keyboardHook.Start();

        _trayIcon = new TrayIconService(
            appState,
            onToggleRecording: () => _recording.ToggleRecording(),
            onToggleFloatingButton: ToggleFloatingButton,
            getMicrophones: () => _audioCapture.GetInputDevices(),
            settingsStore: _settingsStore,
            onShowSettings: ShowTrayPanel,
            onShowAdvancedSettings: ShowAdvancedSettings,
            onQuit: Shutdown);
        _trayIcon.Start();
        _audioCapture.ApplyPreBufferSetting();
        StartupService.Apply(settings);

        if (!settings.OnboardingCompleted)
        {
            ShowAdvancedSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _keyboardHook?.Dispose();
        _audioCapture?.Dispose();
        _trayIcon?.Dispose();
        _settingsStore?.Save(((AppStateViewModel)_floatingButton!.DataContext).Settings);
        base.OnExit(e);
    }

    private void ToggleFloatingButton()
    {
        if (_floatingButton is null)
        {
            return;
        }

        if (_floatingButton.IsVisible)
        {
            _floatingButton.Hide();
        }
        else
        {
            _floatingButton.ShowNearTaskbar();
        }
    }

    private void ShowTrayPanel()
    {
        if (_floatingButton?.DataContext is not AppStateViewModel state)
        {
            return;
        }

        if (_settingsStore is null)
        {
            return;
        }

        _trayPanel ??= new TrayPanelWindow(state, _settingsStore, ShowAdvancedSettings);
        _trayPanel.ShowFromSystemTray();
    }

    private void ShowAdvancedSettings()
    {
        if (_floatingButton?.DataContext is not AppStateViewModel state
            || _settingsStore is null
            || _credentialStore is null
            || _openAiProvider is null
            || _audioCapture is null
            || _diagnosticLog is null
            || _usageStats is null)
        {
            return;
        }

        _advancedSettings ??= new AdvancedSettingsWindow(state, _settingsStore, _credentialStore, _openAiProvider, _audioCapture, _diagnosticLog, _usageStats);
        _advancedSettings.Show();
        _advancedSettings.Activate();
    }
}
