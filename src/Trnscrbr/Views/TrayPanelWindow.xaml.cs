using System.ComponentModel;
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
    private readonly Func<IReadOnlyList<AudioInputDevice>> _getMicrophones;
    private readonly Action<bool> _setFloatingButtonVisibility;
    private readonly Action _showAdvanced;
    private bool _loadingMicrophones;

    public TrayPanelWindow(
        AppStateViewModel state,
        AppSettingsStore settingsStore,
        UsageStatsService usageStats,
        Func<IReadOnlyList<AudioInputDevice>> getMicrophones,
        Action<bool> setFloatingButtonVisibility,
        Action showAdvanced)
    {
        InitializeComponent();
        _state = state;
        _settingsStore = settingsStore;
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
