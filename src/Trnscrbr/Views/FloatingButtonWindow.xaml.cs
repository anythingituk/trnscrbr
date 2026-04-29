using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Views;

public partial class FloatingButtonWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly System.Windows.Media.Color IdleGlassColor = System.Windows.Media.Color.FromArgb(78, 54, 145, 255);
    private static readonly System.Windows.Media.Color IdleShellColor = System.Windows.Media.Color.FromArgb(102, 24, 72, 132);
    private static readonly System.Windows.Media.Color RecordingGlassColor = System.Windows.Media.Color.FromArgb(112, 255, 34, 68);
    private static readonly System.Windows.Media.Color RecordingShellColor = System.Windows.Media.Color.FromArgb(112, 132, 20, 36);
    private static readonly System.Windows.Media.Color ProcessingGlassColor = System.Windows.Media.Color.FromArgb(104, 36, 220, 108);
    private static readonly System.Windows.Media.Color ProcessingShellColor = System.Windows.Media.Color.FromArgb(108, 20, 112, 58);
    private static readonly System.Windows.Media.Color WarningGlassColor = System.Windows.Media.Color.FromArgb(86, 255, 214, 102);

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
    public event EventHandler? PasteLastTranscriptRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? QuitRequested;

    public void ShowNearTaskbar()
    {
        var area = GetTargetScreenWorkArea();
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 12;
        Show();
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
                Width = 76;
                Shell.Width = 46;
                Shell.Height = 28;
                Shell.CornerRadius = new CornerRadius(14);
                Shell.Background = new SolidColorBrush(RecordingShellColor);
                GlowHalo.Width = 52;
                GlowHalo.Height = 34;
                GlowHalo.Fill = new SolidColorBrush(RecordingGlassColor);
                break;
            case RecordingState.Processing:
                Width = 76;
                Shell.Width = 46;
                Shell.Height = 28;
                Shell.CornerRadius = new CornerRadius(14);
                Shell.Background = new SolidColorBrush(ProcessingShellColor);
                GlowHalo.Width = 52;
                GlowHalo.Height = 34;
                GlowHalo.Fill = new SolidColorBrush(ProcessingGlassColor);
                break;
            case RecordingState.Error:
                Width = 220;
                Shell.Width = 28;
                Shell.Height = 28;
                Shell.CornerRadius = new CornerRadius(14);
                Shell.Background = new SolidColorBrush(WarningGlassColor);
                GlowHalo.Width = 40;
                GlowHalo.Height = 34;
                GlowHalo.Fill = new SolidColorBrush(WarningGlassColor);
                break;
            default:
                Width = 76;
                Shell.Width = 28;
                Shell.Height = 28;
                Shell.CornerRadius = new CornerRadius(14);
                Shell.Background = new SolidColorBrush(IdleShellColor);
                GlowHalo.Width = 40;
                GlowHalo.Height = 34;
                GlowHalo.Fill = new SolidColorBrush(IdleGlassColor);
                break;
        }

        AnimateWaveform();
    }

    private void AnimateWaveform()
    {
        _animationPhase += 0.22;
        _smoothedInputLevel = (_smoothedInputLevel * 0.72) + (_state.InputLevel * 0.28);

        if (_state.RecordingState == RecordingState.Idle)
        {
            SetIdleLine();
            var pulse = 0.52 + (Math.Sin(_animationPhase * 0.65) * 0.08);
            GlowHalo.Opacity = pulse;
            return;
        }

        var activeLevel = _state.RecordingState == RecordingState.Recording
            ? Math.Max(0.18, Math.Min(1.0, _smoothedInputLevel * 2.4))
            : _state.RecordingState == RecordingState.Processing
                ? 0.42
                : 0.14;

        SetBarHeight(Bar1, 4, 7, activeLevel, 0.0);
        SetBarHeight(Bar2, 5, 12, activeLevel, 1.1);
        SetBarHeight(Bar3, 4, 9, activeLevel, 2.0);
        SetBarHeight(Bar4, 5, 13, activeLevel, 2.9);
        SetBarHeight(Bar5, 4, 8, activeLevel, 3.8);

        GlowHalo.Opacity = 0.68;
    }

    private void SetIdleLine()
    {
        Bar1.Height = 3;
        Bar2.Height = 3;
        Bar3.Height = 3;
        Bar4.Height = 3;
        Bar5.Height = 3;
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
        var pasteItem = new System.Windows.Controls.MenuItem
        {
            Header = "Paste Last Transcript",
            IsEnabled = HasRecoverableTranscript()
        };
        pasteItem.Click += (_, _) => PasteLastTranscriptRequested?.Invoke(this, EventArgs.Empty);
        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(recordItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Show/Hide Floating Button", IsEnabled = false });
        menu.Items.Add(settingsItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(quitItem);
        menu.IsOpen = true;
    }

    private bool HasRecoverableTranscript()
    {
        return !string.IsNullOrWhiteSpace(_state.LastTranscript)
            && _state.LastTranscriptExpiresAt > DateTimeOffset.Now;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private Rect GetTargetScreenWorkArea()
    {
        var foregroundWindow = GetForegroundWindow();
        var ownWindow = new WindowInteropHelper(this).Handle;
        var screen = foregroundWindow != IntPtr.Zero && foregroundWindow != ownWindow
            ? System.Windows.Forms.Screen.FromHandle(foregroundWindow)
            : System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);

        return DeviceRectToWindowRect(screen.WorkingArea);
    }

    private Rect DeviceRectToWindowRect(System.Drawing.Rectangle rectangle)
    {
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

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
