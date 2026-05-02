using System.Windows;
using Trnscrbr.Services;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Views;

public partial class OnboardingWindow : Window
{
    private readonly AppStateViewModel _state;
    private readonly AppSettingsStore _settingsStore;
    private readonly Action _showLocalSetup;

    public OnboardingWindow(
        AppStateViewModel state,
        AppSettingsStore settingsStore,
        Action showLocalSetup)
    {
        InitializeComponent();
        _state = state;
        _settingsStore = settingsStore;
        _showLocalSetup = showLocalSetup;
        DataContext = state;
    }

    private void Skip_OnClick(object sender, RoutedEventArgs e)
    {
        CompleteOnboarding();
        Close();
    }

    private void Setup_OnClick(object sender, RoutedEventArgs e)
    {
        _state.Settings.ProviderMode = "Local mode";
        _state.Settings.ProviderName = "Local";
        _state.Settings.ActiveEngine = "Local Whisper";
        CompleteOnboarding();
        Close();
        _showLocalSetup();
    }

    private void CompleteOnboarding()
    {
        _state.Settings.OnboardingCompleted = true;
        _settingsStore.Save(_state.Settings);
        _state.RaiseSettingsChanged();
    }
}
