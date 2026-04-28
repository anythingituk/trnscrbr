using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Views;

public partial class FloatingButtonWindow : Window
{
    private readonly AppStateViewModel _state;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private System.Windows.Point? _dragStart;
    private bool _dragging;
    private double _animationPhase;
    private double _smoothedInputLevel;

    public FloatingButtonWindow(AppStateViewModel state)
    {
        InitializeComponent();
        _state = state;
        DataContext = state;
        _state.PropertyChanged += StateOnPropertyChanged;
        _hideTimer.Tick += (_, _) =>
        {
            if (_state.RecordingState is RecordingState.Idle or RecordingState.Error)
            {
                Hide();
            }
        };
        _animationTimer.Tick += (_, _) => AnimateWaveform();
        _animationTimer.Start();
        ApplyState();
    }

    public event EventHandler? ToggleRecordingRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? QuitRequested;

    public void ShowNearTaskbar()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 12;
        Show();
        Activate();
        _hideTimer.Stop();
    }

    public void ShowTransient()
    {
        if (!IsVisible)
        {
            ShowNearTaskbar();
        }

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void StateOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppStateViewModel.RecordingState)
            or nameof(AppStateViewModel.InputLevel)
            or nameof(AppStateViewModel.Elapsed)
            or nameof(AppStateViewModel.StatusMessage))
        {
            Dispatcher.Invoke(ApplyState);
        }
    }

    private void ApplyState()
    {
        TimerText.Visibility = _state.Elapsed >= TimeSpan.FromMinutes(1)
            ? Visibility.Visible
            : Visibility.Collapsed;
        TimerText.Text = $"{(int)_state.Elapsed.TotalMinutes}:{_state.Elapsed.Seconds:00}";

        MessageBubble.Visibility = _state.RecordingState == RecordingState.Error
            ? Visibility.Visible
            : Visibility.Collapsed;

        switch (_state.RecordingState)
        {
            case RecordingState.Recording:
                Width = 118;
                Shell.Width = 64;
                Shell.Height = 38;
                Shell.CornerRadius = new CornerRadius(19);
                GlowHalo.Width = 92;
                GlowHalo.Height = 62;
                GlowHalo.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(130, 255, 92, 56));
                break;
            case RecordingState.Processing:
                Width = 118;
                Shell.Width = 64;
                Shell.Height = 38;
                Shell.CornerRadius = new CornerRadius(19);
                GlowHalo.Width = 92;
                GlowHalo.Height = 62;
                GlowHalo.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 255, 214, 102));
                break;
            case RecordingState.Error:
                Width = 240;
                Shell.Width = 38;
                Shell.Height = 38;
                Shell.CornerRadius = new CornerRadius(19);
                GlowHalo.Width = 72;
                GlowHalo.Height = 60;
                GlowHalo.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(105, 255, 214, 102));
                break;
            default:
                Width = 118;
                Shell.Width = 38;
                Shell.Height = 38;
                Shell.CornerRadius = new CornerRadius(19);
                GlowHalo.Width = 72;
                GlowHalo.Height = 60;
                GlowHalo.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(115, 54, 213, 211));
                break;
        }

        AnimateWaveform();
    }

    private void AnimateWaveform()
    {
        _animationPhase += 0.22;
        _smoothedInputLevel = (_smoothedInputLevel * 0.72) + (_state.InputLevel * 0.28);

        var activeLevel = _state.RecordingState == RecordingState.Recording
            ? Math.Max(0.18, Math.Min(1.0, _smoothedInputLevel * 2.4))
            : _state.RecordingState == RecordingState.Processing
                ? 0.42
                : 0.14;

        SetBarHeight(Bar1, 6, 10, activeLevel, 0.0);
        SetBarHeight(Bar2, 7, 18, activeLevel, 1.1);
        SetBarHeight(Bar3, 5, 14, activeLevel, 2.0);
        SetBarHeight(Bar4, 7, 19, activeLevel, 2.9);
        SetBarHeight(Bar5, 6, 11, activeLevel, 3.8);

        var pulse = 0.84 + (Math.Sin(_animationPhase * 0.65) * 0.12);
        GlowHalo.Opacity = _state.RecordingState == RecordingState.Idle ? pulse : 0.95;
    }

    private void SetBarHeight(FrameworkElement bar, double min, double range, double level, double offset)
    {
        var movement = (Math.Sin(_animationPhase + offset) + 1) / 2;
        var height = min + (range * ((level * 0.72) + (movement * 0.28)));
        bar.Height = Math.Round(height);
    }

    private void Root_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragging = false;
        Root.CaptureMouse();
    }

    private void Root_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (!_dragging && (Math.Abs(current.X - _dragStart.Value.X) > 4 || Math.Abs(current.Y - _dragStart.Value.Y) > 4))
        {
            _dragging = true;
            DragMove();
        }
    }

    private void Root_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Root.ReleaseMouseCapture();
        _dragStart = null;

        if (!_dragging)
        {
            ToggleRecordingRequested?.Invoke(this, EventArgs.Empty);
        }

        _dragging = false;
    }

    private void Root_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var recordItem = new System.Windows.Controls.MenuItem { Header = _state.RecordingState == RecordingState.Recording ? "Stop Recording" : "Start Recording" };
        recordItem.Click += (_, _) => ToggleRecordingRequested?.Invoke(this, EventArgs.Empty);
        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(recordItem);
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Show/Hide Floating Button", IsEnabled = false });
        menu.Items.Add(settingsItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(quitItem);
        menu.IsOpen = true;
    }
}
