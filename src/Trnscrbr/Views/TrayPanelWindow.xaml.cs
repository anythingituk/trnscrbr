using System.Windows;
using Trnscrbr.Services;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Views;

public partial class TrayPanelWindow : Window
{
    private readonly AppStateViewModel _state;
    private readonly AppSettingsStore _settingsStore;
    private readonly Action _showAdvanced;

    public TrayPanelWindow(AppStateViewModel state, AppSettingsStore settingsStore, Action showAdvanced)
    {
        InitializeComponent();
        _state = state;
        _settingsStore = settingsStore;
        DataContext = state;
        _showAdvanced = showAdvanced;
        Deactivated += (_, _) => Hide();
    }

    public void ShowFromSystemTray()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 8;
        Top = area.Bottom - Height - 8;
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

    private void Persist()
    {
        _settingsStore.Save(_state.Settings);
        StartupService.Apply(_state.Settings);
        _state.RaiseSettingsChanged();
    }
}
