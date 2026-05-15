using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace IsConnected;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int PingTimeoutMs = 1_500;

    private const string PrimaryTargetHost = "8.8.8.8";
    private const string FallbackTargetHost = "1.1.1.1";
    private static readonly int[] IntervalOptions = [5, 10, 30, 60, 300];

    private readonly NotifyIcon trayIcon;
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem statusItem;
    private readonly ToolStripMenuItem autostartItem;
    private readonly ToolStripMenuItem highlightIssueItem;
    private readonly ToolStripMenuItem intervalMenu;
    private readonly System.Windows.Forms.Timer timer;
    private readonly Icon onlineIcon;
    private readonly Icon offlineIcon;
    private readonly AppSettings settings;
    private readonly IssueHighlightOverlay issueHighlightOverlay;

    private bool checkInProgress;
    private bool isOnline;
    private bool isExiting;
    private bool suppressAutostartChange;
    private bool suppressHighlightIssueChange;

    public TrayApplicationContext()
    {
        settings = AppSettings.Load();
        onlineIcon = TrayIconFactory.CreateNetworkIcon(Color.FromArgb(38, 166, 91), false);
        offlineIcon = TrayIconFactory.CreateNetworkIcon(Color.FromArgb(220, 53, 69), true);

        statusItem = new ToolStripMenuItem("Checking internet...") { Enabled = false };
        autostartItem = new ToolStripMenuItem("Autostart") { CheckOnClick = true };
        autostartItem.Checked = AutostartManager.IsEnabled();
        autostartItem.CheckedChanged += (_, _) => TrySetAutostart(autostartItem.Checked);

        highlightIssueItem = new ToolStripMenuItem("Highlight issue") { CheckOnClick = true };
        highlightIssueItem.Checked = settings.HighlightIssue;
        highlightIssueItem.CheckedChanged += (_, _) => TrySetHighlightIssue(highlightIssueItem.Checked);

        intervalMenu = new ToolStripMenuItem("Ping interval");
        RebuildIntervalMenu();

        var checkNowItem = new ToolStripMenuItem("Check now");
        checkNowItem.Click += async (_, _) => await CheckConnectivityAsync();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autostartItem);
        menu.Items.Add(highlightIssueItem);
        menu.Items.Add(intervalMenu);
        menu.Items.Add(checkNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        trayIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = offlineIcon,
            Text = "IsConnected: checking...",
            Visible = true
        };
        trayIcon.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                menu.Show(Cursor.Position);
            }
        };

        issueHighlightOverlay = new IssueHighlightOverlay();

        timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += async (_, _) =>
        {
            ApplyTimerInterval();
            await CheckConnectivityAsync();
        };
        timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            isExiting = true;
            timer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            issueHighlightOverlay.Dispose();
            menu.Dispose();
            timer.Dispose();
            onlineIcon.Dispose();
            offlineIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task CheckConnectivityAsync()
    {
        if (checkInProgress || isExiting)
        {
            return;
        }

        checkInProgress = true;

        try
        {
            var primaryResult = await TryPingAsync(PrimaryTargetHost);
            if (primaryResult.Success)
            {
                SetStatus(true, primaryResult.RoundtripMs, PrimaryTargetHost);
                return;
            }

            var fallbackResult = await TryPingAsync(FallbackTargetHost);
            if (fallbackResult.Success)
            {
                SetStatus(true, fallbackResult.RoundtripMs, FallbackTargetHost);
                return;
            }

            SetStatus(false, null, null);
        }
        catch
        {
            SetStatus(false, null, null);
        }
        finally
        {
            checkInProgress = false;
        }
    }

    private static async Task<(bool Success, long? RoundtripMs)> TryPingAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, PingTimeoutMs);
            return reply.Status == IPStatus.Success
                ? (true, reply.RoundtripTime)
                : (false, null);
        }
        catch
        {
            return (false, null);
        }
    }

    private void SetStatus(bool online, long? roundtripMs, string? host)
    {
        if (isExiting)
        {
            return;
        }

        isOnline = online;
        trayIcon.Icon = isOnline ? onlineIcon : offlineIcon;

        var checkedAt = DateTime.Now.ToString("HH:mm:ss");
        var statusText = isOnline
            ? $"Online, {roundtripMs} ms via {host}"
            : "Offline";

        statusItem.Text = $"{statusText} (last check {checkedAt})";
        trayIcon.Text = $"IsConnected: {statusText}";
        UpdateIssueHighlight();
    }

    private void RebuildIntervalMenu()
    {
        intervalMenu.DropDownItems.Clear();

        foreach (var seconds in IntervalOptions)
        {
            var item = new ToolStripMenuItem(FormatInterval(seconds))
            {
                CheckOnClick = true,
                Checked = settings.IntervalSeconds == seconds,
                Tag = seconds
            };

            item.Click += (_, _) =>
            {
                var previousIntervalSeconds = settings.IntervalSeconds;
                settings.IntervalSeconds = seconds;
                try
                {
                    settings.Save();
                    ApplyTimerInterval();
                    RebuildIntervalMenu();
                }
                catch (Exception ex)
                {
                    settings.IntervalSeconds = previousIntervalSeconds;
                    ApplyTimerInterval();
                    RebuildIntervalMenu();
                    ShowError("Could not save ping interval", ex);
                }
            };

            intervalMenu.DropDownItems.Add(item);
        }
    }

    private void ApplyTimerInterval()
    {
        timer.Interval = Math.Max(1, settings.IntervalSeconds) * 1_000;
    }

    private static string FormatInterval(int seconds) =>
        seconds < 60 ? $"{seconds} seconds" : $"{seconds / 60} minutes";

    private void TrySetAutostart(bool enabled)
    {
        if (suppressAutostartChange)
        {
            return;
        }

        try
        {
            AutostartManager.SetEnabled(enabled);
        }
        catch (Exception ex)
        {
            suppressAutostartChange = true;
            autostartItem.Checked = !enabled;
            suppressAutostartChange = false;
            ShowError("Could not update autostart", ex);
        }
    }

    private void TrySetHighlightIssue(bool enabled)
    {
        if (suppressHighlightIssueChange)
        {
            return;
        }

        var previousValue = settings.HighlightIssue;
        settings.HighlightIssue = enabled;

        try
        {
            settings.Save();
            UpdateIssueHighlight();
        }
        catch (Exception ex)
        {
            settings.HighlightIssue = previousValue;
            suppressHighlightIssueChange = true;
            highlightIssueItem.Checked = previousValue;
            suppressHighlightIssueChange = false;
            UpdateIssueHighlight();
            ShowError("Could not save issue highlight setting", ex);
        }
    }

    private void UpdateIssueHighlight()
    {
        issueHighlightOverlay.SetVisible(settings.HighlightIssue && !isOnline && !isExiting);
    }

    private void ShowError(string message, Exception ex)
    {
        if (isExiting)
        {
            return;
        }

        statusItem.Text = $"{message}: {ex.Message}";
        trayIcon.ShowBalloonTip(4_000, "IsConnected", message, ToolTipIcon.Warning);
    }
}

internal sealed class AppSettings
{
    private const int DefaultIntervalSeconds = 30;

    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;
    public bool HighlightIssue { get; set; }

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IsConnected",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            if (settings is null || !IntervalIsSupported(settings.IntervalSeconds))
            {
                return new AppSettings();
            }

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static bool IntervalIsSupported(int seconds) => seconds is 5 or 10 or 30 or 60 or 300;
}

internal sealed class IssueHighlightOverlay : Form
{
    private const int OverlayWidth = 120;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    public IssueHighlightOverlay()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(220, 53, 69);
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        Opacity = 0.45;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            // Keeps the visual warning out of Alt+Tab, prevents focus stealing, and lets clicks pass through.
            createParams.ExStyle |= WsExToolWindow | WsExNoActivate | WsExTransparent;
            return createParams;
        }
    }

    public void SetVisible(bool visible)
    {
        if (visible)
        {
            PositionOnPrimaryScreen();
            if (!Visible)
            {
                Show();
            }

            return;
        }

        if (Visible)
        {
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var brush = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(30, 220, 53, 69),
            Color.FromArgb(255, 220, 53, 69),
            LinearGradientMode.Horizontal);

        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    private void PositionOnPrimaryScreen()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? Screen.FromControl(this).Bounds;
        Bounds = new Rectangle(
            bounds.Right - OverlayWidth,
            bounds.Top,
            OverlayWidth,
            bounds.Height);
    }
}

internal static class AutostartManager
{
    private const string AppName = "IsConnected";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(AppName) as string;
            return string.Equals(value, Quote(GetExecutablePath()), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (enabled)
        {
            if (key is null)
            {
                throw new InvalidOperationException("Windows Run registry key is unavailable.");
            }

            key.SetValue(AppName, Quote(GetExecutablePath()), RegistryValueKind.String);
            return;
        }

        key?.DeleteValue(AppName, false);
    }

    private static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        return Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
    }

    private static string Quote(string value) => $"\"{value}\"";
}

internal static class TrayIconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon CreateNetworkIcon(Color color, bool crossedOut)
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var pen = new Pen(color, 6)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var fill = new SolidBrush(color);

        graphics.DrawLine(pen, 14, 45, 50, 45);
        graphics.DrawLine(pen, 32, 45, 32, 22);
        graphics.DrawLine(pen, 20, 22, 44, 22);
        graphics.FillEllipse(fill, 9, 40, 10, 10);
        graphics.FillEllipse(fill, 27, 40, 10, 10);
        graphics.FillEllipse(fill, 45, 40, 10, 10);
        graphics.FillEllipse(fill, 27, 17, 10, 10);

        if (crossedOut)
        {
            using var slashBack = new Pen(Color.White, 12)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var slash = new Pen(color, 7)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            graphics.DrawLine(slashBack, 12, 12, 52, 52);
            graphics.DrawLine(slash, 12, 12, 52, 52);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
