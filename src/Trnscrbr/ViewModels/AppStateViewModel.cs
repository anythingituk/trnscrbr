using System.ComponentModel;
using System.Runtime.CompilerServices;
using Trnscrbr.Models;

namespace Trnscrbr.ViewModels;

public sealed class AppStateViewModel : INotifyPropertyChanged
{
    private RecordingState _recordingState = RecordingState.Idle;
    private string _statusMessage = "Ready";
    private string? _lastTranscript;
    private DateTimeOffset? _lastTranscriptExpiresAt;
    private double _inputLevel;
    private TimeSpan _elapsed;

    public AppStateViewModel(AppSettings settings)
    {
        Settings = settings;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSettings Settings { get; }

    public RecordingState RecordingState
    {
        get => _recordingState;
        set => SetField(ref _recordingState, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string? LastTranscript
    {
        get => _lastTranscript;
        set => SetField(ref _lastTranscript, value);
    }

    public DateTimeOffset? LastTranscriptExpiresAt
    {
        get => _lastTranscriptExpiresAt;
        set => SetField(ref _lastTranscriptExpiresAt, value);
    }

    public double InputLevel
    {
        get => _inputLevel;
        set => SetField(ref _inputLevel, Math.Clamp(value, 0, 1));
    }

    public TimeSpan Elapsed
    {
        get => _elapsed;
        set => SetField(ref _elapsed, value);
    }

    public bool IsProviderConfigured => Settings.ProviderMode != "Not configured";

    public string ActiveEngineLabel => Settings.ActiveEngine == "None"
        ? "No engine configured"
        : Settings.ActiveEngine;

    public void RaiseSettingsChanged()
    {
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(IsProviderConfigured));
        OnPropertyChanged(nameof(ActiveEngineLabel));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum RecordingState
{
    Idle,
    Recording,
    Processing,
    Error
}
