using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly UsageStatsService _usageStats;
    private readonly CredentialStore _credentialStore = new();
    private readonly LocalModelDownloadService _localModelDownload = new();
    private readonly LocalWhisperToolDownloadService _localWhisperToolDownload = new();
    private readonly LocalModeRepairService _localModeRepair;
    private readonly Func<IReadOnlyList<AudioInputDevice>> _getMicrophones;
    private readonly Action _showAdvanced;
    private bool _loadingMicrophones;
    private bool _loadingLocalModels;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

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
        _usageStats = usageStats;
        _getMicrophones = getMicrophones;
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
        RefreshCostWarning();
        UpdateLayout();
        var panelHeight = GetPlacementHeight();
        Left = Math.Max(area.Left + 8, area.Right - Width - trayOverflowAvoidanceWidth);
        Top = Math.Max(area.Top + 8, area.Bottom - panelHeight - bottomOffset);
        BringToForeground();
    }

    private double GetPlacementHeight()
    {
        if (ActualHeight > 0)
        {
            return Math.Min(MaxHeight, ActualHeight);
        }

        if (Content is FrameworkElement content)
        {
            content.Measure(new System.Windows.Size(Width, MaxHeight));
            if (content.DesiredSize.Height > 0)
            {
                return Math.Min(MaxHeight, content.DesiredSize.Height);
            }
        }

        return MaxHeight;
    }

    private void BringToForeground()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Topmost = true;
        Show();
        Activate();
        Focus();
        TrayLocalModelComboBox.Focus();

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            SetForegroundWindow(handle);
        }

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                Topmost = false;
                Activate();
                TrayLocalModelComboBox.Focus();
            }),
            DispatcherPriority.ApplicationIdle);
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
                RefreshCostWarning();
            });
        }
    }

    private void RefreshHotkeySummary()
    {
        HotkeySummaryText.Text = _state.Settings.GlobalHotkeysEnabled
            ? $"Hotkeys: toggle {FormatHotkey(_state.Settings.ToggleRecordingHotkey)}; push-to-talk {FormatHotkey(_state.Settings.PushToTalkHotkey)}."
            : "Global hotkeys disabled. Use tray controls.";
    }

    private void RefreshCostWarning()
    {
        CostWarningPanel.Visibility = Visibility.Collapsed;

        if (!string.Equals(_state.Settings.ProviderMode, "Bring your own API key", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var threshold = (double)_state.Settings.MonthlyCostWarning;
        if (threshold <= 0)
        {
            return;
        }

        var month = _usageStats.GetCurrentMonth();
        if (month.EstimatedCostUsd < threshold)
        {
            return;
        }

        CostWarningText.Text = $"OpenAI monthly estimate ${month.EstimatedCostUsd:0.00} has reached your ${threshold:0.00} warning.";
        CostWarningPanel.Visibility = Visibility.Visible;
    }

    private void RefreshLocalModels()
    {
        _loadingLocalModels = true;
        try
        {
            var options = BuildAiModelOptions();
            TrayLocalModelComboBox.ItemsSource = options;
            var selected = SelectCurrentAiModelOption(options);
            TrayLocalModelComboBox.SelectedItem = selected;
            RefreshAiModelHelp(selected);
        }
        finally
        {
            _loadingLocalModels = false;
        }
    }

    private void AiModel_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingLocalModels || TrayLocalModelComboBox.SelectedItem is not TrayAiModelOption option)
        {
            return;
        }

        if (option.Kind == AiModelKind.OpenAi)
        {
            _state.Settings.ProviderMode = "Bring your own API key";
            _state.Settings.ProviderName = "OpenAI";
            _state.Settings.ActiveEngine = "OpenAI";
            Persist();
            RefreshAiModelHelp(option);
            return;
        }

        if (option.Kind == AiModelKind.LocalChatCleanup)
        {
            if (!IsLocalModeReady())
            {
                TrayLocalModelComboBox.ToolTip = "Run Free Quick Setup in main settings before using local chat cleanup.";
                RefreshLocalModels();
                return;
            }

            _state.Settings.ProviderMode = "Local mode";
            _state.Settings.ProviderName = "Local";
            _state.Settings.ActiveEngine = "Local AI";
            _state.Settings.CleanupMode = "Rewrite";
            Persist();
            RefreshAiModelHelp(option);
            return;
        }

        if (option.LocalPreset is not { } preset)
        {
            return;
        }

        var modelPath = Path.Combine(_localModelDownload.ModelsDirectory, preset.FileName);
        if (!File.Exists(modelPath))
        {
            TrayLocalModelComboBox.ToolTip = $"{preset.DisplayName} needs to be downloaded in main settings.";
            RefreshLocalModels();
            return;
        }

        _state.Settings.LocalWhisperModelPath = modelPath;
        _state.Settings.LocalWhisperModelPresetId = preset.Id;
        _state.Settings.ProviderMode = "Local mode";
        _state.Settings.ProviderName = "Local";
        _state.Settings.ActiveEngine = "Local AI";
        Persist();
        RefreshAiModelHelp(option);
    }

    private void RefreshAiModelHelp(TrayAiModelOption? selected)
    {
        ApiKeySetupButton.Visibility = Visibility.Collapsed;

        if (selected is null)
        {
            TrayLocalModelComboBox.ToolTip = "Choose an AI model in main settings.";
            return;
        }

        if (selected.Kind == AiModelKind.OpenAi && !_credentialStore.HasOpenAiApiKey())
        {
            TrayLocalModelComboBox.ToolTip = "OpenAI API key needed before this model can be used.";
            ApiKeySetupButton.Visibility = Visibility.Visible;
            return;
        }

        TrayLocalModelComboBox.ToolTip = selected.Help;
    }

    private void ApiKeySetup_OnClick(object sender, RoutedEventArgs e)
    {
        Persist();
        _showAdvanced();
    }

    private IReadOnlyList<TrayAiModelOption> BuildAiModelOptions()
    {
        var options = new List<TrayAiModelOption>
        {
            new(
                "openai",
                "OpenAI API model",
                "Uses your configured OpenAI API key.",
                AiModelKind.OpenAi,
                null)
        };

        options.AddRange(LocalModelDownloadService.Presets.Select(preset => new TrayAiModelOption(
            $"local:{preset.Id}",
            $"Local AI - {preset.DisplayName}",
            FormatModelPresetGuidance(preset),
            AiModelKind.LocalSpeech,
            preset)));

        if (!string.IsNullOrWhiteSpace(_state.Settings.LocalLlmModel))
        {
            options.Add(new TrayAiModelOption(
                $"chat:{_state.Settings.LocalLlmModel}",
                $"Local chat cleanup - {_state.Settings.LocalLlmModel}",
                "Uses local AI for dictation, then rewrites with your configured local chat model.",
                AiModelKind.LocalChatCleanup,
                null));
        }

        return options;
    }

    private TrayAiModelOption? SelectCurrentAiModelOption(IReadOnlyList<TrayAiModelOption> options)
    {
        if (string.Equals(_state.Settings.ProviderMode, "Bring your own API key", StringComparison.OrdinalIgnoreCase))
        {
            return options.FirstOrDefault(option => option.Kind == AiModelKind.OpenAi);
        }

        if (string.Equals(_state.Settings.CleanupMode, "Rewrite", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_state.Settings.LocalLlmModel))
        {
            var localChat = options.FirstOrDefault(option => option.Kind == AiModelKind.LocalChatCleanup);
            if (localChat is not null)
            {
                return localChat;
            }
        }

        var localPreset = _localModelDownload.FindPreset(
                _state.Settings.LocalWhisperModelPath,
                _state.Settings.LocalWhisperModelPresetId)
            ?? LocalModelDownloadService.Presets.FirstOrDefault(candidate => candidate.Id == "small");

        return localPreset is null
            ? options.FirstOrDefault()
            : options.FirstOrDefault(option =>
                option.LocalPreset is not null
                && string.Equals(option.LocalPreset.Id, localPreset.Id, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatModelPresetGuidance(LocalModelPreset preset)
    {
        return preset.Id switch
        {
            "small" => $"Recommended for most users. {preset.Description}",
            "medium" => $"Slower on many PCs. {preset.Description}",
            "large" => $"Can be very slow on CPU-only local AI. {preset.Description}",
            _ => preset.Description
        };
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

    private enum AiModelKind
    {
        OpenAi,
        LocalSpeech,
        LocalChatCleanup
    }

    private sealed record TrayAiModelOption(
        string Id,
        string Label,
        string Help,
        AiModelKind Kind,
        LocalModelPreset? LocalPreset)
    {
        public override string ToString()
        {
            return Label;
        }
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
