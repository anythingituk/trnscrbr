using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Trnscrbr.Services;
using Trnscrbr.ViewModels;
using Trnscrbr.Views;

namespace Trnscrbr;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private SingleInstanceService? _singleInstance;
    private AppSettingsStore? _settingsStore;
    private KeyboardHookService? _keyboardHook;
    private TrayIconService? _trayIcon;
    private AudioCaptureService? _audioCapture;
    private CredentialStore? _credentialStore;
    private OpenAiProviderService? _openAiProvider;
    private LocalProviderService? _localProvider;
    private DiagnosticLogService? _diagnosticLog;
    private EnvironmentDiagnosticsService? _environmentDiagnostics;
    private UsageStatsService? _usageStats;
    private SettingsImportExportService? _settingsImportExport;
    private RecordingCoordinator? _recording;
    private DispatcherTimer? _lastTranscriptTimer;
    private FloatingButtonWindow? _floatingButton;
    private OnboardingWindow? _onboarding;
    private TrayPanelWindow? _trayPanel;
    private AdvancedSettingsWindow? _advancedSettings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyWindowsTheme();
        SystemEvents.UserPreferenceChanged += SystemEventsOnUserPreferenceChanged;

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceService.MutexName, out var createdNew);
        if (!createdNew)
        {
            SingleInstanceService.NotifyExistingInstance();
            Shutdown();
            return;
        }

        _settingsStore = new AppSettingsStore();
        _credentialStore = new CredentialStore();
        _diagnosticLog = new DiagnosticLogService();
        RegisterExceptionHandlers(_diagnosticLog);
        _environmentDiagnostics = new EnvironmentDiagnosticsService(_diagnosticLog);
        _environmentDiagnostics.LogStartupSnapshot();
        _openAiProvider = new OpenAiProviderService(_diagnosticLog);
        _localProvider = new LocalProviderService(_diagnosticLog);
        _usageStats = new UsageStatsService();
        _settingsImportExport = new SettingsImportExportService();
        var settings = _settingsStore.Load();
        var appState = new AppStateViewModel(settings);
        StartLastTranscriptExpiryTimer(appState);
        _singleInstance = new SingleInstanceService(() => Dispatcher.BeginInvoke(ShowTrayPanel));
        _singleInstance.Start();

        _floatingButton = new FloatingButtonWindow(appState);
        _audioCapture = new AudioCaptureService(appState);
        var insertion = new TextInsertionService(appState, _diagnosticLog);
        _recording = new RecordingCoordinator(appState, insertion, _floatingButton, _audioCapture, _credentialStore, _openAiProvider, _localProvider, _diagnosticLog, _usageStats, ShowTrayPanel);
        _floatingButton.ToggleRecordingRequested += (_, _) => _recording.ToggleRecording();
        _floatingButton.PasteLastTranscriptRequested += (_, _) => _recording.PasteLastTranscript();
        _floatingButton.SettingsRequested += (_, _) => ShowTrayPanel();
        _floatingButton.QuitRequested += (_, _) => Shutdown();

        _keyboardHook = new KeyboardHookService(appState);
        _keyboardHook.PushToTalkPressed += (_, _) => _recording.HandlePushToTalkPressed();
        _keyboardHook.PushToTalkReleased += (_, _) => _recording.HandlePushToTalkReleased();
        _keyboardHook.ToggleRecordingPressed += (_, _) => _recording.ToggleRecording();
        _keyboardHook.CancelPressed += (_, _) => _recording.Cancel();
        try
        {
            _keyboardHook.Start();
        }
        catch (Exception ex)
        {
            _diagnosticLog.Error("Keyboard hook startup failed", ex);
            appState.RecordingState = RecordingState.Error;
            appState.StatusMessage = "Hotkeys unavailable. Use tray controls.";
        }

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
            ShowOnboarding();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _keyboardHook?.Dispose();
            _audioCapture?.Dispose();
            _trayIcon?.Dispose();
            _singleInstance?.Dispose();
            _lastTranscriptTimer?.Stop();
            _lastTranscriptTimer = null;

            if (_settingsStore is not null && _floatingButton?.DataContext is AppStateViewModel state)
            {
                _settingsStore.Save(state.Settings);
            }

            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            SystemEvents.UserPreferenceChanged -= SystemEventsOnUserPreferenceChanged;
        }
        catch (Exception ex)
        {
            _diagnosticLog?.Error("Shutdown cleanup failed", ex);
        }

        base.OnExit(e);
    }

    private void SystemEventsOnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Dispatcher.BeginInvoke(ApplyWindowsTheme);
        }
    }

    private void ApplyWindowsTheme()
    {
        var light = IsWindowsLightTheme();

        SetBrush("AppBackgroundBrush", light ? "#EEF3F6" : "#101820");
        SetBrush("PanelBackgroundBrush", light ? "#F8FBFC" : "#14222B");
        SetBrush("SurfaceBrush", light ? "#FFFFFF" : "#17252F");
        SetBrush("PanelForegroundBrush", light ? "#15202B" : "#ECF7FA");
        SetBrush("MutedForegroundBrush", light ? "#617080" : "#A6B7C2");
        SetBrush("SubtleBorderBrush", light ? "#D9E3E8" : "#2F4652");
        SetBrush("ControlBackgroundBrush", light ? "#FFFFFF" : "#101B23");
        SetBrush("ControlHoverBrush", light ? "#F8FEFE" : "#1C303A");
        SetBrush("ControlSelectedBrush", light ? "#DFF4F7" : "#203D49");
        SetBrush("AccentSoftBrush", light ? "#E8F8F8" : "#163B43");
        SetBrush("StatusBackgroundBrush", light ? "#EEF8F8" : "#112F36");
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }

    private void SetBrush(string key, string color)
    {
        if (Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        }
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

    private void StartLastTranscriptExpiryTimer(AppStateViewModel state)
    {
        _lastTranscriptTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _lastTranscriptTimer.Tick += (_, _) =>
        {
            if (state.LastTranscriptExpiresAt is not null
                && state.LastTranscriptExpiresAt <= DateTimeOffset.Now)
            {
                state.LastTranscript = null;
                state.LastTranscriptExpiresAt = null;
            }
        };
        _lastTranscriptTimer.Start();
    }

    private void ShowRecoverableError(Exception exception)
    {
        if (_floatingButton?.DataContext is not AppStateViewModel state)
        {
            return;
        }

        state.RecordingState = RecordingState.Error;
        state.StatusMessage = $"Recovered from error: {exception.Message}";
        try
        {
            _floatingButton.ShowTransient();
        }
        catch (Exception showException)
        {
            _diagnosticLog?.Error("Floating button error display failed", showException);
        }
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

        if (_settingsStore is null || _audioCapture is null || _localProvider is null || _diagnosticLog is null || _usageStats is null)
        {
            return;
        }

        _trayPanel ??= new TrayPanelWindow(
            state,
            _settingsStore,
            _audioCapture,
            _localProvider,
            _diagnosticLog,
            _usageStats,
            () => _audioCapture?.GetInputDevices() ?? [],
            SetFloatingButtonVisibility,
            ShowAdvancedSettings);
        _trayPanel.ShowFromSystemTray();
    }

    private void ShowOnboarding()
    {
        if (_floatingButton?.DataContext is not AppStateViewModel state
            || _settingsStore is null)
        {
            return;
        }

        if (_onboarding is null)
        {
            _onboarding = new OnboardingWindow(state, _settingsStore, ShowLocalSetup);
            _onboarding.Closed += (_, _) => _onboarding = null;
        }

        _onboarding.Show();
        _onboarding.Activate();
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

    private void ShowLocalSetup()
    {
        ShowAdvancedSettings();
        _advancedSettings?.SelectLocalModelsTab();
    }
}
