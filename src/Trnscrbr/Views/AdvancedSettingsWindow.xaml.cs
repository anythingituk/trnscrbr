using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Trnscrbr.Services;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Views;

public partial class AdvancedSettingsWindow : Window
{
    private readonly AppStateViewModel _state;
    private readonly AppSettingsStore _settingsStore;

    public AdvancedSettingsWindow(AppStateViewModel state, AppSettingsStore settingsStore)
    {
        InitializeComponent();
        _state = state;
        _settingsStore = settingsStore;
        DataContext = state;
        VocabularyBox.Text = string.Join(Environment.NewLine, state.Settings.CustomVocabulary);
        Closing += (_, args) =>
        {
            args.Cancel = true;
            Persist();
            Hide();
        };
    }

    private void OnboardingComplete_OnClick(object sender, RoutedEventArgs e)
    {
        _state.Settings.OnboardingCompleted = true;
        Persist();
    }

    private void TestConnection_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Provider test is not implemented yet. The key can be saved with a warning.", "Trnscrbr");
    }

    private void SaveProvider_OnClick(object sender, RoutedEventArgs e)
    {
        _state.Settings.ProviderMode = "Bring your own API key";
        _state.Settings.ActiveEngine = "OpenAI";
        Persist();
        MessageBox.Show("Provider saved with warning. Secure API key storage is still to be implemented.", "Trnscrbr");
    }

    private void CopyDiagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        var diagnostics = $"""
            Trnscrbr diagnostics
            App version: 0.1.0
            Provider: {_state.Settings.ProviderName}
            Provider mode: {_state.Settings.ProviderMode}
            Active engine: {_state.Settings.ActiveEngine}
            API key present: {(ApiKeyBox.Password.Length > 0 ? "yes" : "no")}
            Microphone: {_state.Settings.MicrophoneName}
            Hotkeys: Ctrl+Win+Space, Esc, Ctrl+Win+V
            Transcript content: redacted
            Raw audio: redacted
            """;

        Clipboard.SetText(diagnostics);
    }

    private void Hyperlink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Persist()
    {
        _state.Settings.CustomVocabulary = VocabularyBox.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _settingsStore.Save(_state.Settings);
        _state.RaiseSettingsChanged();
    }
}
