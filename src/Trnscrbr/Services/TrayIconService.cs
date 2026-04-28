using System.Drawing;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Windows.Forms;
using Trnscrbr.Models;
using Trnscrbr.ViewModels;

namespace Trnscrbr.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly AppStateViewModel _state;
    private readonly Action _onToggleRecording;
    private readonly Action _onToggleFloatingButton;
    private readonly Func<IReadOnlyList<AudioInputDevice>> _getMicrophones;
    private readonly Action _onShowSettings;
    private readonly Action _onShowAdvancedSettings;
    private readonly Action _onQuit;
    private NotifyIcon? _notifyIcon;
    private Icon? _currentIcon;

    public TrayIconService(
        AppStateViewModel state,
        Action onToggleRecording,
        Action onToggleFloatingButton,
        Func<IReadOnlyList<AudioInputDevice>> getMicrophones,
        Action onShowSettings,
        Action onShowAdvancedSettings,
        Action onQuit)
    {
        _state = state;
        _onToggleRecording = onToggleRecording;
        _onToggleFloatingButton = onToggleFloatingButton;
        _getMicrophones = getMicrophones;
        _onShowSettings = onShowSettings;
        _onShowAdvancedSettings = onShowAdvancedSettings;
        _onQuit = onQuit;
    }

    public void Start()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateStateIcon(_state.RecordingState),
            Text = "Trnscrbr",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _currentIcon = _notifyIcon.Icon;
        _state.PropertyChanged += StateOnPropertyChanged;

        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                _onShowSettings();
            }
        };
    }

    public void Dispose()
    {
        _state.PropertyChanged -= StateOnPropertyChanged;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _currentIcon?.Dispose();
        _currentIcon = null;
    }

    private void StateOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppStateViewModel.RecordingState))
        {
            UpdateIcon();
        }
    }

    private void UpdateIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        var previous = _currentIcon;
        _currentIcon = CreateStateIcon(_state.RecordingState);
        _notifyIcon.Icon = _currentIcon;
        previous?.Dispose();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var recordItem = new ToolStripMenuItem("Start Recording", null, (_, _) => _onToggleRecording());
        var showItem = new ToolStripMenuItem("Show/Hide Floating Button", null, (_, _) => _onToggleFloatingButton());
        var micMenu = new ToolStripMenuItem("Microphone");

        menu.Opening += (_, _) =>
        {
            recordItem.Text = _state.RecordingState == RecordingState.Recording ? "Stop Recording" : "Start Recording";
            RebuildMicrophoneMenu(micMenu);
        };

        menu.Items.Add(recordItem);
        menu.Items.Add(showItem);
        menu.Items.Add(micMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => _onShowSettings()));
        menu.Items.Add(new ToolStripMenuItem("Advanced Settings", null, (_, _) => _onShowAdvancedSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => _onQuit()));
        return menu;
    }

    private void RebuildMicrophoneMenu(ToolStripMenuItem micMenu)
    {
        micMenu.DropDownItems.Clear();

        foreach (var device in _getMicrophones())
        {
            var item = new ToolStripMenuItem(device.Name, null, (_, _) =>
            {
                _state.Settings.MicrophoneName = device.Name;
                _state.RaiseSettingsChanged();
            })
            {
                Checked = device.Name == _state.Settings.MicrophoneName
            };

            micMenu.DropDownItems.Add(item);
        }
    }

    private static Icon CreateStateIcon(RecordingState state)
    {
        var color = state switch
        {
            RecordingState.Recording => Color.FromArgb(255, 92, 56),
            RecordingState.Processing => Color.FromArgb(255, 214, 102),
            RecordingState.Error => Color.FromArgb(255, 190, 80),
            _ => Color.FromArgb(54, 213, 211)
        };

        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var glowBrush = new SolidBrush(Color.FromArgb(80, color));
        graphics.FillEllipse(glowBrush, 3, 3, 26, 26);

        using var fillBrush = new SolidBrush(Color.FromArgb(235, color));
        graphics.FillEllipse(fillBrush, 8, 8, 16, 16);

        using var shineBrush = new SolidBrush(Color.FromArgb(190, Color.White));
        graphics.FillEllipse(shineBrush, 11, 10, 5, 5);

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
