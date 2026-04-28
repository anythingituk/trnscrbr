using System.Windows;
using Trnscrbr.Services;
using Trnscrbr.ViewModels;
using Trnscrbr.Views;

namespace Trnscrbr;

public partial class App : Application
{
    private AppSettingsStore? _settingsStore;
    private KeyboardHookService? _keyboardHook;
    private TrayIconService? _trayIcon;
    private RecordingCoordinator? _recording;
    private FloatingButtonWindow? _floatingButton;
    private TrayPanelWindow? _trayPanel;
    private AdvancedSettingsWindow? _advancedSettings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsStore = new AppSettingsStore();
        var settings = _settingsStore.Load();
        var appState = new AppStateViewModel(settings);

        _floatingButton = new FloatingButtonWindow(appState);
        var insertion = new TextInsertionService(appState);
        _recording = new RecordingCoordinator(appState, insertion, _floatingButton);
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
            onShowSettings: ShowTrayPanel,
            onShowAdvancedSettings: ShowAdvancedSettings,
            onQuit: Shutdown);
        _trayIcon.Start();

        if (!settings.OnboardingCompleted)
        {
            ShowAdvancedSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _keyboardHook?.Dispose();
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

        _trayPanel ??= new TrayPanelWindow(state, ShowAdvancedSettings);
        _trayPanel.ShowFromSystemTray();
    }

    private void ShowAdvancedSettings()
    {
        if (_floatingButton?.DataContext is not AppStateViewModel state || _settingsStore is null)
        {
            return;
        }

        _advancedSettings ??= new AdvancedSettingsWindow(state, _settingsStore);
        _advancedSettings.Show();
        _advancedSettings.Activate();
    }
}
