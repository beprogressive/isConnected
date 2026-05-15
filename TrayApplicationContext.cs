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
    private const int EdgeWidth = 1;
    private const int GlowSize = 4;
    private const byte MaxGlowAlpha = 255;
    private const double PulsePeriodMs = 2_800d;
    private const double MinPulseIntensity = 0.45d;
    private const int AcSrcOver = 0x00;
    private const int AcSrcAlpha = 0x01;
    private const int UlwAlpha = 0x00000002;
    private const int WmNchittest = 0x0084;
    private const int Httransparent = -1;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly System.Windows.Forms.Timer pulseTimer;
    private readonly Stopwatch pulseStopwatch = new();

    public IssueHighlightOverlay()
    {
        AutoScaleMode = AutoScaleMode.None;
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        pulseTimer = new System.Windows.Forms.Timer { Interval = 33 };
        pulseTimer.Tick += (_, _) => RenderGlow();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            // Keeps the visual warning out of Alt+Tab, prevents focus stealing, and lets clicks pass through.
            createParams.ExStyle |= WsExLayered | WsExToolWindow | WsExNoActivate | WsExTransparent;
            return createParams;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNchittest)
        {
            message.Result = Httransparent;
            return;
        }

        base.WndProc(ref message);
    }

    public void SetVisible(bool visible)
    {
        if (visible)
        {
            PositionOnPrimaryScreen();
            if (!Visible)
            {
                Show();
                pulseStopwatch.Restart();
                pulseTimer.Start();
            }

            RenderGlow();
            return;
        }

        if (Visible)
        {
            pulseTimer.Stop();
            pulseStopwatch.Reset();
            Hide();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pulseTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PositionOnPrimaryScreen()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? Screen.FromControl(this).Bounds;
        Bounds = bounds;
    }

    private void RenderGlow()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.Clear(Color.Transparent);
            DrawGlow(graphics, bitmap.Size, GetPulseIntensity());
        }

        ApplyLayeredBitmap(bitmap);
    }

    private double GetPulseIntensity()
    {
        if (!pulseStopwatch.IsRunning)
        {
            return 1d;
        }

        var progress = pulseStopwatch.Elapsed.TotalMilliseconds % PulsePeriodMs / PulsePeriodMs;
        var wave = (Math.Sin(progress * Math.Tau - Math.PI / 2d) + 1d) / 2d;
        return MinPulseIntensity + (1d - MinPulseIntensity) * wave;
    }

    private static void DrawGlow(Graphics graphics, Size size, double pulseIntensity)
    {
        var maxDepth = Math.Min(GlowSize, Math.Min(size.Width, size.Height) / 2);
        for (var offset = 0; offset < maxDepth; offset++)
        {
            var alpha = GetGlowAlpha(offset, pulseIntensity);
            using var brush = new SolidBrush(Color.FromArgb(alpha, 255, 0, 0));

            graphics.FillRectangle(brush, 0, offset, size.Width, 1);
            graphics.FillRectangle(brush, 0, size.Height - offset - 1, size.Width, 1);
            graphics.FillRectangle(brush, offset, 0, 1, size.Height);
            graphics.FillRectangle(brush, size.Width - offset - 1, 0, 1, size.Height);
        }

        using var edgeBrush = new SolidBrush(Color.FromArgb(ScaleAlpha(MaxGlowAlpha, pulseIntensity), 255, 0, 0));
        graphics.FillRectangle(edgeBrush, 0, 0, size.Width, EdgeWidth);
        graphics.FillRectangle(edgeBrush, 0, size.Height - EdgeWidth, size.Width, EdgeWidth);
        graphics.FillRectangle(edgeBrush, 0, 0, EdgeWidth, size.Height);
        graphics.FillRectangle(edgeBrush, size.Width - EdgeWidth, 0, EdgeWidth, size.Height);
    }

    private static byte GetGlowAlpha(int offset, double pulseIntensity)
    {
        var distance = Math.Max(0d, 1d - (double)offset / GlowSize);
        return ScaleAlpha(MaxGlowAlpha * distance * distance, pulseIntensity);
    }

    private static byte ScaleAlpha(double alpha, double pulseIntensity) =>
        (byte)Math.Round(Math.Clamp(alpha * pulseIntensity, 0d, byte.MaxValue));

    private void ApplyLayeredBitmap(Bitmap bitmap)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        var previousObject = SelectObject(memoryDc, bitmapHandle);

        try
        {
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var source = new NativePoint(0, 0);
            var destination = new NativePoint(Left, Top);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha,
            };

            UpdateLayeredWindow(
                Handle,
                screenDc,
                ref destination,
                ref size,
                memoryDc,
                ref source,
                0,
                ref blend,
                UlwAlpha);
        }
        finally
        {
            SelectObject(memoryDc, previousObject);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hWnd,
        IntPtr hdcDst,
        ref NativePoint pptDst,
        ref NativeSize pSize,
        IntPtr hdcSrc,
        ref NativePoint pptSrc,
        int crKey,
        ref BlendFunction pBlend,
        int dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
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
