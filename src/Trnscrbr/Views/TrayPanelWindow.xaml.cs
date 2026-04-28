using System.Windows;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Views;

public partial class TrayPanelWindow : Window
{
    private readonly Action _showAdvanced;

    public TrayPanelWindow(AppStateViewModel state, Action showAdvanced)
    {
        InitializeComponent();
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
        _showAdvanced();
    }
}
