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
    private Point? _dragStart;
    private bool _dragging;

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
        var level = Math.Max(0.12, _state.InputLevel);
        Bar1.Height = 10 + (level * 14);
        Bar2.Height = 14 + (level * 24);
        Bar3.Height = 8 + (level * 18);
        Bar4.Height = 16 + (level * 20);
        Bar5.Height = 10 + (level * 16);

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
                Width = 132;
                Shell.Width = 112;
                Shell.CornerRadius = new CornerRadius(34);
                Glow.Color = Color.FromRgb(255, 92, 56);
                Glow.BlurRadius = 34;
                break;
            case RecordingState.Processing:
                Width = 132;
                Shell.Width = 112;
                Shell.CornerRadius = new CornerRadius(34);
                Glow.Color = Color.FromRgb(255, 214, 102);
                Glow.BlurRadius = 32;
                break;
            case RecordingState.Error:
                Width = 280;
                Shell.Width = 68;
                Shell.CornerRadius = new CornerRadius(34);
                Glow.Color = Color.FromRgb(255, 214, 102);
                Glow.BlurRadius = 22;
                break;
            default:
                Width = 104;
                Shell.Width = 68;
                Shell.CornerRadius = new CornerRadius(34);
                Glow.Color = Color.FromRgb(54, 213, 211);
                Glow.BlurRadius = 28;
                break;
        }
    }

    private void Root_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragging = false;
        Root.CaptureMouse();
    }

    private void Root_OnMouseMove(object sender, MouseEventArgs e)
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
