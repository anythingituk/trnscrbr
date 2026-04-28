using System.Windows;
using System.Windows.Threading;
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
    private SettingsImportExportService? _settingsImportExport;
    private RecordingCoordinator? _recording;
    private FloatingButtonWindow? _floatingButton;
    private TrayPanelWindow? _trayPanel;
    private AdvancedSettingsWindow? _advancedSettings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsStore = new AppSettingsStore();
        _credentialStore = new CredentialStore();
        _diagnosticLog = new DiagnosticLogService();
        RegisterExceptionHandlers(_diagnosticLog);
        _openAiProvider = new OpenAiProviderService(_diagnosticLog);
        _usageStats = new UsageStatsService();
        _settingsImportExport = new SettingsImportExportService();
        var settings = _settingsStore.Load();
        var appState = new AppStateViewModel(settings);

        _floatingButton = new FloatingButtonWindow(appState);
        _audioCapture = new AudioCaptureService(appState);
        var insertion = new TextInsertionService(appState, _diagnosticLog);
        _recording = new RecordingCoordinator(appState, insertion, _floatingButton, _audioCapture, _credentialStore, _openAiProvider, _diagnosticLog, _usageStats);
        _floatingButton.ToggleRecordingRequested += (_, _) => _recording.ToggleRecording();
        _floatingButton.PasteLastTranscriptRequested += (_, _) => _recording.PasteLastTranscript();
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
            onPasteLastTranscript: () => _recording.PasteLastTranscript(),
            getMicrophones: () => _audioCapture.GetInputDevices(),
            settingsStore: _settingsStore,
            onShowSettings: ShowTrayPanel,
            onShowAdvancedSettings: ShowAdvancedSettings,
            onQuit: Shutdown);
        _trayIcon.Start();
        _audioCapture.ApplyPreBufferSetting();
        StartupService.Apply(settings);
        if (settings.FloatingButtonEnabled)
        {
            _floatingButton.ShowNearTaskbar();
        }

        if (!settings.OnboardingCompleted)
        {
            ShowAdvancedSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _keyboardHook?.Dispose();
            _audioCapture?.Dispose();
            _trayIcon?.Dispose();

            if (_settingsStore is not null && _floatingButton?.DataContext is AppStateViewModel state)
            {
                _settingsStore.Save(state.Settings);
            }
        }
        catch (Exception ex)
        {
            _diagnosticLog?.Error("Shutdown cleanup failed", ex);
        }

        base.OnExit(e);
    }

    private void RegisterExceptionHandlers(DiagnosticLogService diagnosticLog)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            diagnosticLog.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
            ShowRecoverableError(args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                diagnosticLog.Error("Unhandled process exception", exception, new Dictionary<string, string>
                {
                    ["isTerminating"] = args.IsTerminating.ToString()
                });
            }
            else
            {
                diagnosticLog.Error("Unhandled process exception", metadata: new Dictionary<string, string>
                {
                    ["exceptionObject"] = args.ExceptionObject?.ToString() ?? string.Empty,
                    ["isTerminating"] = args.IsTerminating.ToString()
                });
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            diagnosticLog.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    private void ShowRecoverableError(Exception exception)
    {
        if (_floatingButton?.DataContext is not AppStateViewModel state)
        {
            return;
        }

        state.RecordingState = RecordingState.Error;
        state.StatusMessage = "Trnscrbr recovered from an error. See Diagnostics.";
        _floatingButton.ShowTransient();
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
            if (_floatingButton.DataContext is AppStateViewModel hiddenState)
            {
                hiddenState.Settings.FloatingButtonEnabled = false;
                _settingsStore?.Save(hiddenState.Settings);
                hiddenState.RaiseSettingsChanged();
            }
        }
        else
        {
            _floatingButton.ShowNearTaskbar();
            if (_floatingButton.DataContext is AppStateViewModel shownState)
            {
                shownState.Settings.FloatingButtonEnabled = true;
                _settingsStore?.Save(shownState.Settings);
                shownState.RaiseSettingsChanged();
            }
        }
    }

    private void SetFloatingButtonVisibility(bool visible)
    {
        if (_floatingButton?.DataContext is not AppStateViewModel state)
        {
            return;
        }

        state.Settings.FloatingButtonEnabled = visible;
        _settingsStore?.Save(state.Settings);
        state.RaiseSettingsChanged();

        if (visible)
        {
            _floatingButton.ShowNearTaskbar();
        }
        else
        {
            _floatingButton.Hide();
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

        _trayPanel ??= new TrayPanelWindow(
            state,
            _settingsStore,
            () => _audioCapture?.GetInputDevices() ?? [],
            () => _recording?.ToggleRecording(),
            SetFloatingButtonVisibility,
            ShowAdvancedSettings);
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
            || _usageStats is null
            || _settingsImportExport is null)
        {
            return;
        }

        _advancedSettings ??= new AdvancedSettingsWindow(
            state,
            _settingsStore,
            _credentialStore,
            _openAiProvider,
            _audioCapture,
            _diagnosticLog,
            _usageStats,
            _settingsImportExport);
        _advancedSettings.Show();
        _advancedSettings.Activate();
    }
}
