using System.Drawing;
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
            Icon = SystemIcons.Application,
            Text = "Trnscrbr",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

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
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
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
}
