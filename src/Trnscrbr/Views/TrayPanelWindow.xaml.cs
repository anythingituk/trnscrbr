using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using Trnscrbr.Models;
using Trnscrbr.Services;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Views;

public partial class TrayPanelWindow : Window
{
    private readonly AppStateViewModel _state;
    private readonly AppSettingsStore _settingsStore;
    private readonly AudioCaptureService _audioCapture;
    private readonly LocalProviderService _localProvider;
    private readonly LocalTestPhraseService _localTestPhrase;
    private readonly DiagnosticLogService _diagnosticLog;
    private readonly LocalModelDownloadService _localModelDownload = new();
    private readonly LocalWhisperToolDownloadService _localWhisperToolDownload = new();
    private readonly LocalModeRepairService _localModeRepair;
    private readonly Func<IReadOnlyList<AudioInputDevice>> _getMicrophones;
    private readonly Action<bool> _setFloatingButtonVisibility;
    private readonly Action _showAdvanced;
    private bool _loadingMicrophones;

    public TrayPanelWindow(
        AppStateViewModel state,
        AppSettingsStore settingsStore,
        AudioCaptureService audioCapture,
        LocalProviderService localProvider,
        DiagnosticLogService diagnosticLog,
        UsageStatsService usageStats,
        Func<IReadOnlyList<AudioInputDevice>> getMicrophones,
        Action<bool> setFloatingButtonVisibility,
        Action showAdvanced)
    {
        InitializeComponent();
        _localModeRepair = new LocalModeRepairService(_localWhisperToolDownload, _localModelDownload);
        _state = state;
        _settingsStore = settingsStore;
        _audioCapture = audioCapture;
        _localProvider = localProvider;
        _localTestPhrase = new LocalTestPhraseService(audioCapture, localProvider);
        _diagnosticLog = diagnosticLog;
        _getMicrophones = getMicrophones;
        _setFloatingButtonVisibility = setFloatingButtonVisibility;
        DataContext = state;
        _showAdvanced = showAdvanced;
        _state.PropertyChanged += State_OnPropertyChanged;
        Deactivated += (_, _) => Hide();
    }

    public void ShowFromSystemTray()
    {
        var area = GetCurrentScreenWorkArea();
        const double trayOverflowAvoidanceWidth = 260;
        const double bottomOffset = 72;

        RefreshMicrophones();
        RefreshLocalReadiness();
        RefreshHotkeySummary();
        Left = Math.Max(area.Left + 8, area.Right - Width - trayOverflowAvoidanceWidth);
        Top = Math.Max(area.Top + 8, area.Bottom - Height - bottomOffset);
        Show();
        Activate();
        AdvancedButton.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _state.PropertyChanged -= State_OnPropertyChanged;
        base.OnClosed(e);
    }

    private void Advanced_OnClick(object sender, RoutedEventArgs e)
    {
        Persist();
        _showAdvanced();
    }

    private async void LocalReadinessAction_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsLocalModeReady())
        {
            Persist();
            _showAdvanced();
            return;
        }

        SetLocalTestControlsEnabled(false);
        LocalReadinessActionButton.Content = "Repairing";
        LocalTestResultText.Text = "Repairing local mode...";

        try
        {
            var progress = new Progress<string>(message =>
            {
                LocalTestResultText.Text = message;
                _state.StatusMessage = message;
            });
            var downloadProgress = new Progress<double>(value =>
            {
                LocalTestResultText.Text = $"Repairing local mode: {value:P0}";
            });

            var result = await _localModeRepair.RepairAsync(
                _state.Settings,
                progress,
                downloadProgress);

            Persist();
            RefreshLocalReadiness();
            LocalTestResultText.Text = $"{FormatRepairResult(result)} Click Test to confirm it works.";
            _state.StatusMessage = "Local mode repaired";
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            LocalTestResultText.Text = LocalSetupErrorFormatter.Format("Local mode repair failed", ex);
            _state.StatusMessage = "Local mode repair failed";
            _diagnosticLog.Error("Tray local mode repair failed", ex);
        }
        finally
        {
            SetLocalTestControlsEnabled(true);
            RefreshLocalReadiness();
        }
    }

    private async void LocalReadinessTest_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_state.IsProviderConfigured || !IsLocalModeReady())
        {
            LocalTestResultText.Text = "Run Free Quick Setup before testing a local phrase.";
            _showAdvanced();
            return;
        }

        if (_state.RecordingState is RecordingState.Recording or RecordingState.Processing)
        {
            LocalTestResultText.Text = "Finish the current recording before testing a local phrase.";
            return;
        }

        SetLocalTestControlsEnabled(false);
        LocalTestResultText.Text = "Recording test phrase. Speak now for 5 seconds...";
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            var result = await _localTestPhrase.RunAsync(_state, message => LocalTestResultText.Text = message, timeout.Token);
            LocalTestResultText.Text = string.IsNullOrWhiteSpace(result.Transcript)
                ? result.Message
                : $"Local test transcript: {result.Transcript.Trim()}";
            _state.StatusMessage = result.NoInputCaptured
                ? $"No input from {_state.Settings.MicrophoneName}. Choose another microphone."
                : "Local test completed";
            _state.RecordingState = result.NoInputCaptured
                ? RecordingState.Error
                : RecordingState.Idle;
        }
        catch (OperationCanceledException)
        {
            LocalTestResultText.Text = "Local test phrase timed out.";
            _state.StatusMessage = "Local test timed out";
            _state.RecordingState = RecordingState.Idle;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            LocalTestResultText.Text = LocalSetupErrorFormatter.Format("Local test phrase failed", ex);
            _state.StatusMessage = "Local test failed";
            _state.RecordingState = RecordingState.Error;
            _diagnosticLog.Error("Tray local test phrase failed", ex);
        }
        finally
        {
            SetLocalTestControlsEnabled(true);
        }
    }

    private void Window_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void Settings_OnChanged(object sender, RoutedEventArgs e)
    {
        Persist();
    }

    private void FloatingButton_OnClick(object sender, RoutedEventArgs e)
    {
        _setFloatingButtonVisibility(_state.Settings.FloatingButtonEnabled);
    }

    private void Microphone_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingMicrophones || MicrophoneBox.SelectedValue is not string microphoneName)
        {
            return;
        }

        _state.Settings.MicrophoneName = microphoneName;
        Persist();
    }

    private void RefreshMicrophones()
    {
        _loadingMicrophones = true;
        try
        {
            var devices = _getMicrophones();
            MicrophoneBox.ItemsSource = devices;

            var selected = devices.Any(device => device.Name == _state.Settings.MicrophoneName)
                ? _state.Settings.MicrophoneName
                : devices.FirstOrDefault(device => device.IsDefault)?.Name ?? devices.FirstOrDefault()?.Name;

            MicrophoneBox.SelectedValue = selected;
            if (!string.IsNullOrWhiteSpace(selected) && selected != _state.Settings.MicrophoneName)
            {
                _state.Settings.MicrophoneName = selected;
                Persist();
            }
        }
        finally
        {
            _loadingMicrophones = false;
        }
    }

    private void State_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppStateViewModel.Settings) or nameof(AppStateViewModel.IsProviderConfigured))
        {
            Dispatcher.Invoke(() =>
            {
                RefreshLocalReadiness();
                RefreshHotkeySummary();
            });
        }
    }

    private void RefreshHotkeySummary()
    {
        HotkeySummaryText.Text = $"Hotkeys: toggle {FormatHotkey(_state.Settings.ToggleRecordingHotkey)}; push-to-talk {FormatHotkey(_state.Settings.PushToTalkHotkey)}.";
    }

    private void RefreshLocalReadiness()
    {
        var settings = _state.Settings;
        var usingLocalMode = IsLocalModeActive();
        var hasCli = HasLocalWhisperCli();
        var hasModel = HasLocalWhisperModel();

        if (usingLocalMode && hasCli && hasModel)
        {
            LocalReadinessPanel.Background = (System.Windows.Media.Brush)FindResource("StatusBackgroundBrush");
            LocalReadinessTitleText.Text = "Local mode ready";
            LocalReadinessDetailText.Text = $"Using {_state.ActiveEngineLabel}. Dictation is free and private.";
            LocalReadinessActionButton.Content = "Details";
            LocalReadinessTestButton.IsEnabled = true;
            return;
        }

        LocalReadinessPanel.Background = System.Windows.Media.Brushes.Transparent;
        LocalReadinessActionButton.Content = "Repair";
        LocalReadinessTestButton.IsEnabled = false;

        if (!usingLocalMode)
        {
            LocalReadinessTitleText.Text = "Free local mode not active";
            LocalReadinessDetailText.Text = "Open setup to enable free dictation without an API key.";
            return;
        }

        if (!hasCli && !hasModel)
        {
            LocalReadinessTitleText.Text = "Local setup needed";
            LocalReadinessDetailText.Text = "Whisper CLI and model are missing. Run Free Quick Setup.";
            return;
        }

        LocalReadinessTitleText.Text = "Local setup needs repair";
        LocalReadinessDetailText.Text = hasCli
            ? "The local Whisper model is missing. Open setup to repair it."
            : "The whisper.cpp CLI is missing. Open setup to repair it.";
    }

    private void SetLocalTestControlsEnabled(bool enabled)
    {
        LocalReadinessActionButton.IsEnabled = enabled;
        LocalReadinessTestButton.IsEnabled = enabled && IsLocalModeReady();
        MicrophoneBox.IsEnabled = enabled;
    }

    private static string FormatRepairResult(LocalModeRepairResult result)
    {
        var details = string.Join(" ", result.Steps.Select(step => $"{step.Name}: {step.Detail}"));
        return string.IsNullOrWhiteSpace(details)
            ? result.Message
            : $"{result.Message} {details}";
    }

    private static string FormatHotkey(string hotkey)
    {
        return hotkey.Replace("+", " + ", StringComparison.Ordinal);
    }

    private bool IsLocalModeReady()
    {
        return IsLocalModeActive() && HasLocalWhisperCli() && HasLocalWhisperModel();
    }

    private bool IsLocalModeActive()
    {
        return string.Equals(_state.Settings.ProviderMode, "Local mode", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasLocalWhisperCli()
    {
        return File.Exists(_state.Settings.LocalWhisperExecutablePath);
    }

    private bool HasLocalWhisperModel()
    {
        return File.Exists(_state.Settings.LocalWhisperModelPath);
    }

    private void Persist()
    {
        _settingsStore.Save(_state.Settings);
        StartupService.Apply(_state.Settings);
        _state.RaiseSettingsChanged();
    }

    private Rect GetCurrentScreenWorkArea()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var rectangle = screen.WorkingArea;
        var topLeft = new System.Windows.Point(rectangle.Left, rectangle.Top);
        var bottomRight = new System.Windows.Point(rectangle.Right, rectangle.Bottom);
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice;

        if (transform is not null)
        {
            topLeft = transform.Value.Transform(topLeft);
            bottomRight = transform.Value.Transform(bottomRight);
        }
        else
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            topLeft = new System.Windows.Point(topLeft.X / dpi.DpiScaleX, topLeft.Y / dpi.DpiScaleY);
            bottomRight = new System.Windows.Point(bottomRight.X / dpi.DpiScaleX, bottomRight.Y / dpi.DpiScaleY);
        }

        return new Rect(topLeft, bottomRight);
    }
}
