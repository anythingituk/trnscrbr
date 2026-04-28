using System.Windows;
using Trnscrbr.Models;
using Trnscrbr.Services;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Views;

public partial class TrayPanelWindow : Window
{
    private readonly AppStateViewModel _state;
    private readonly AppSettingsStore _settingsStore;
    private readonly Func<IReadOnlyList<AudioInputDevice>> _getMicrophones;
    private readonly Action _showAdvanced;
    private bool _loadingMicrophones;

    public TrayPanelWindow(
        AppStateViewModel state,
        AppSettingsStore settingsStore,
        Func<IReadOnlyList<AudioInputDevice>> getMicrophones,
        Action showAdvanced)
    {
        InitializeComponent();
        _state = state;
        _settingsStore = settingsStore;
        _getMicrophones = getMicrophones;
        DataContext = state;
        _showAdvanced = showAdvanced;
        Deactivated += (_, _) => Hide();
    }

    public void ShowFromSystemTray()
    {
        var area = SystemParameters.WorkArea;
        const double trayOverflowAvoidanceWidth = 260;
        const double bottomOffset = 72;

        RefreshMicrophones();
        Left = Math.Max(area.Left + 8, area.Right - Width - trayOverflowAvoidanceWidth);
        Top = Math.Max(area.Top + 8, area.Bottom - Height - bottomOffset);
        Show();
        Activate();
    }

    private void Advanced_OnClick(object sender, RoutedEventArgs e)
    {
        Persist();
        _showAdvanced();
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
        }
        finally
        {
            _loadingMicrophones = false;
        }
    }

    private void Persist()
    {
        _settingsStore.Save(_state.Settings);
        StartupService.Apply(_state.Settings);
        _state.RaiseSettingsChanged();
    }
}
