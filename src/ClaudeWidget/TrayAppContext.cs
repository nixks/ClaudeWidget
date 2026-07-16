using ClaudeWidget.Core;
using ClaudeWidget.UI;

namespace ClaudeWidget;

public sealed class TrayAppContext : ApplicationContext
{
    private static readonly int[] PollChoices = [30, 60, 120, 300];

    private readonly TrayIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly UsageClient _client;
    private readonly Settings _settings;
    private readonly ThresholdNotifier _notifier;
    private readonly FlyoutForm _flyout = new();
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _intervalMenu;

    private UsageSnapshot? _snapshot;
    private int _consecutiveFailures;
    private bool _authError;
    private bool _polling;

    public TrayAppContext()
    {
        _settings = Settings.Load(Settings.DefaultPath);
        _client = new UsageClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, new CredentialsReader());
        _notifier = new ThresholdNotifier(_settings.WarnThreshold, _settings.CriticalThreshold);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh now", null, async (_, _) => await PollAsync());

        _autostartItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = Autostart.IsEnabled(),
        };
        _autostartItem.CheckedChanged += OnAutostartToggled;
        menu.Items.Add(_autostartItem);

        _intervalMenu = new ToolStripMenuItem("Poll interval");
        foreach (var seconds in PollChoices)
        {
            var item = new ToolStripMenuItem(seconds < 60 ? $"{seconds} s" : $"{seconds / 60} min")
            {
                Tag = seconds,
                Checked = seconds == _settings.PollIntervalSeconds,
            };
            item.Click += OnIntervalSelected;
            _intervalMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(_intervalMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new TrayIcon { ContextMenuStrip = menu };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleFlyout();
        };
        _flyout.PositionChanged += p =>
        {
            _settings.FlyoutX = p.X;
            _settings.FlyoutY = p.Y;
            _settings.Save(Settings.DefaultPath);
        };
        _flyout.RefreshRequested += async () => await PollAsync();
        UpdateTray();

        _timer = new System.Windows.Forms.Timer { Interval = _settings.PollIntervalSeconds * 1000 };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
        _ = PollAsync();
    }

    private bool IsStale => _consecutiveFailures >= 3;

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        _flyout.SetRefreshing(true);
        try
        {
            var result = await _client.FetchAsync();
            switch (result.Status)
            {
                case FetchStatus.Ok:
                    _snapshot = result.Snapshot;
                    _consecutiveFailures = 0;
                    _authError = false;
                    var message = _notifier.Update(_snapshot!);
                    if (message is not null)
                    {
                        _tray.ShowBalloonTip(5000, "Claude usage", message);
                    }
                    break;
                case FetchStatus.AuthError:
                    _authError = true;
                    break;
                case FetchStatus.Failure:
                    _consecutiveFailures++;
                    break;
            }
        }
        catch (Exception)
        {
            _consecutiveFailures++;
        }
        finally
        {
            _polling = false;
            _flyout.SetRefreshing(false);
            UpdateTray();
        }
    }

    private void UpdateTray()
    {
        var percent = _snapshot?.SessionPercent;
        var icon = IconRenderer.Render(
            IconRenderer.IconText(percent, _authError),
            IconRenderer.ColorFor(percent, IsStale, _authError));
        var previous = _tray.Icon;
        _tray.Icon = icon;
        previous?.Dispose();

        var tip = Formatting.Tooltip(_snapshot, IsStale, _authError, DateTimeOffset.UtcNow);
        _tray.Text = tip.Length <= 127 ? tip : tip[..127];

        _flyout.UpdateFrom(_snapshot, IsStale, _authError);
    }

    private void ToggleFlyout()
    {
        if (_flyout.Visible)
        {
            _flyout.Hide();
        }
        else
        {
            _flyout.UpdateFrom(_snapshot, IsStale, _authError);
            _flyout.ShowNearTray(_settings.FlyoutX is int x && _settings.FlyoutY is int y ? new Point(x, y) : null);
        }
    }

    private void OnAutostartToggled(object? sender, EventArgs e)
    {
        if (_autostartItem.Checked) Autostart.Enable(Environment.ProcessPath!);
        else Autostart.Disable();
        _settings.StartWithWindows = _autostartItem.Checked;
        _settings.Save(Settings.DefaultPath);
    }

    private void OnIntervalSelected(object? sender, EventArgs e)
    {
        var item = (ToolStripMenuItem)sender!;
        var seconds = (int)item.Tag!;
        _settings.PollIntervalSeconds = seconds;
        _settings.Save(Settings.DefaultPath);
        _timer.Interval = seconds * 1000;
        foreach (ToolStripMenuItem other in _intervalMenu.DropDownItems)
        {
            other.Checked = Equals(other.Tag, seconds);
        }
    }

    protected override void ExitThreadCore()
    {
        _tray.Icon?.Dispose();
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _timer.Dispose();
        _flyout.Dispose();
        base.ExitThreadCore();
    }
}
