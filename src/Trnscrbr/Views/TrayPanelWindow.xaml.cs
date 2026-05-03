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
    private bool _loadingLocalModels;

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
        RefreshLocalModels();
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
                RefreshLocalModels();
                RefreshHotkeySummary();
            });
        }
    }

    private void RefreshHotkeySummary()
    {
        HotkeySummaryText.Text = _state.Settings.GlobalHotkeysEnabled
            ? $"Hotkeys: toggle {FormatHotkey(_state.Settings.ToggleRecordingHotkey)}; push-to-talk {FormatHotkey(_state.Settings.PushToTalkHotkey)}."
            : "Global hotkeys disabled. Use tray controls.";
    }

    private void RefreshLocalModels()
    {
        _loadingLocalModels = true;
        try
        {
            TrayLocalModelComboBox.ItemsSource = LocalModelDownloadService.Presets;
            var selected = _localModelDownload.FindPreset(
                    _state.Settings.LocalWhisperModelPath,
                    _state.Settings.LocalWhisperModelPresetId)
                ?? LocalModelDownloadService.Presets.FirstOrDefault(candidate => candidate.Id == "small");
            TrayLocalModelComboBox.SelectedItem = selected;
            TrayLocalModelHelpText.Text = selected is null
                ? "Run Free Quick Setup in main settings to enable local AI."
                : selected.Description;
        }
        finally
        {
            _loadingLocalModels = false;
        }
    }

    private void LocalModel_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingLocalModels || TrayLocalModelComboBox.SelectedItem is not LocalModelPreset preset)
        {
            return;
        }

        var modelPath = Path.Combine(_localModelDownload.ModelsDirectory, preset.FileName);
        if (!File.Exists(modelPath))
        {
            TrayLocalModelHelpText.Text = $"{preset.DisplayName} needs to be downloaded in main settings.";
            RefreshLocalModels();
            return;
        }

        _state.Settings.LocalWhisperModelPath = modelPath;
        _state.Settings.LocalWhisperModelPresetId = preset.Id;
        _state.Settings.ProviderMode = "Local mode";
        _state.Settings.ProviderName = "Local";
        _state.Settings.ActiveEngine = "Local AI";
        Persist();
        TrayLocalModelHelpText.Text = $"{preset.DisplayName} selected.";
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
