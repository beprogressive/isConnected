using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private readonly ToolStripMenuItem highlightAreaMenu;
    private readonly ToolStripMenuItem highlightColorItem;
    private readonly ToolStripMenuItem intervalMenu;
    private readonly ToolStripMenuItem testIssueItem;
    private readonly System.Windows.Forms.Timer timer;
    private readonly System.Windows.Forms.Timer issueTestTimer;
    private readonly Icon onlineIcon;
    private readonly Icon offlineIcon;
    private readonly AppSettings settings;
    private readonly IssueHighlightOverlay issueHighlightOverlay;

    private bool checkInProgress;
    private bool isOnline;
    private bool isExiting;
    private bool suppressAutostartChange;
    private bool suppressHighlightIssueChange;
    private bool issueTestActive;

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

        highlightAreaMenu = new ToolStripMenuItem("Highlight area");
        RebuildHighlightAreaMenu();

        highlightColorItem = new ToolStripMenuItem("Highlight color");
        highlightColorItem.Click += (_, _) => TrySetHighlightColor();
        UpdateHighlightColorPreview();

        intervalMenu = new ToolStripMenuItem("Ping interval");
        RebuildIntervalMenu();

        var checkNowItem = new ToolStripMenuItem("Check now");
        checkNowItem.Click += async (_, _) => await CheckConnectivityAsync();

        testIssueItem = new ToolStripMenuItem("Test issue");
        testIssueItem.Click += (_, _) => StartIssueTest();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autostartItem);
        menu.Items.Add(highlightIssueItem);
        menu.Items.Add(highlightAreaMenu);
        menu.Items.Add(highlightColorItem);
        menu.Items.Add(intervalMenu);
        menu.Items.Add(testIssueItem);
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
        ApplyHighlightOptions();

        timer = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += async (_, _) =>
        {
            ApplyTimerInterval();
            await CheckConnectivityAsync();
        };
        timer.Start();

        issueTestTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
        issueTestTimer.Tick += (_, _) => StopIssueTest();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            isExiting = true;
            issueTestTimer.Stop();
            timer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            issueHighlightOverlay.Dispose();
            menu.Dispose();
            issueTestTimer.Dispose();
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
        UpdateIssueEffects();
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

    private void RebuildHighlightAreaMenu()
    {
        highlightAreaMenu.DropDownItems.Clear();

        foreach (var area in Enum.GetValues<HighlightArea>())
        {
            var item = new ToolStripMenuItem(FormatHighlightArea(area))
            {
                CheckOnClick = true,
                Checked = settings.HighlightArea == area,
                Tag = area,
            };

            item.Click += (_, _) => TrySetHighlightArea(area);
            highlightAreaMenu.DropDownItems.Add(item);
        }
    }

    private static string FormatHighlightArea(HighlightArea area) =>
        area switch
        {
            HighlightArea.FullScreen => "Full screen",
            HighlightArea.Left => "Left",
            HighlightArea.Right => "Right",
            HighlightArea.Top => "Top",
            HighlightArea.Bottom => "Bottom",
            HighlightArea.TopLeft => "Top-left corner",
            HighlightArea.TopRight => "Top-right corner",
            HighlightArea.BottomLeft => "Bottom-left corner",
            HighlightArea.BottomRight => "Bottom-right corner",
            _ => area.ToString(),
        };

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
            UpdateIssueEffects();
        }
        catch (Exception ex)
        {
            settings.HighlightIssue = previousValue;
            suppressHighlightIssueChange = true;
            highlightIssueItem.Checked = previousValue;
            suppressHighlightIssueChange = false;
            UpdateIssueEffects();
            ShowError("Could not save issue highlight setting", ex);
        }
    }

    private void TrySetHighlightArea(HighlightArea area)
    {
        var previousValue = settings.HighlightArea;
        settings.HighlightArea = area;

        try
        {
            settings.Save();
            ApplyHighlightOptions();
            RebuildHighlightAreaMenu();
        }
        catch (Exception ex)
        {
            settings.HighlightArea = previousValue;
            ApplyHighlightOptions();
            RebuildHighlightAreaMenu();
            ShowError("Could not save highlight area", ex);
        }
    }

    private void TrySetHighlightColor()
    {
        using var dialog = new ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            SolidColorOnly = false,
            Color = settings.HighlightColor,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var previousValue = settings.HighlightColor;
        settings.HighlightColor = dialog.Color;

        try
        {
            settings.Save();
            ApplyHighlightOptions();
            UpdateHighlightColorPreview();
        }
        catch (Exception ex)
        {
            settings.HighlightColor = previousValue;
            ApplyHighlightOptions();
            UpdateHighlightColorPreview();
            ShowError("Could not save highlight color", ex);
        }
    }

    private void StartIssueTest()
    {
        issueTestActive = true;
        testIssueItem.Enabled = false;
        issueTestTimer.Stop();
        issueTestTimer.Start();
        UpdateIssueEffects();
    }

    private void StopIssueTest()
    {
        issueTestTimer.Stop();
        issueTestActive = false;
        testIssueItem.Enabled = true;
        UpdateIssueEffects();
    }

    private void UpdateIssueEffects()
    {
        var issueActive = (!isOnline || issueTestActive) && !isExiting;
        ApplyHighlightOptions();
        issueHighlightOverlay.SetVisible(settings.HighlightIssue && issueActive);
    }

    private void ApplyHighlightOptions()
    {
        issueHighlightOverlay.SetOptions(new HighlightOptions(settings.HighlightArea, settings.HighlightColor));
    }

    private void UpdateHighlightColorPreview()
    {
        highlightColorItem.Text = $"Highlight color: {FormatColor(settings.HighlightColor)}";
        highlightColorItem.Image?.Dispose();
        highlightColorItem.Image = CreateColorSwatch(settings.HighlightColor);
    }

    private static string FormatColor(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Bitmap CreateColorSwatch(Color color)
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var fill = new SolidBrush(color);
        using var border = new Pen(SystemColors.ControlDark);
        graphics.FillRectangle(fill, 2, 2, 12, 12);
        graphics.DrawRectangle(border, 2, 2, 12, 12);
        return bitmap;
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
    private static readonly Color DefaultHighlightColor = Color.Red;

    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;
    public bool HighlightIssue { get; set; }
    public HighlightArea HighlightArea { get; set; } = HighlightArea.FullScreen;
    public int HighlightColorArgb { get; set; } = DefaultHighlightColor.ToArgb();

    [JsonIgnore]
    public Color HighlightColor
    {
        get => Color.FromArgb(HighlightColorArgb);
        set => HighlightColorArgb = Color.FromArgb(byte.MaxValue, value).ToArgb();
    }

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
            if (settings is null)
            {
                return new AppSettings();
            }

            settings.Normalize();
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

    private void Normalize()
    {
        if (!IntervalIsSupported(IntervalSeconds))
        {
            IntervalSeconds = DefaultIntervalSeconds;
        }

        if (!Enum.IsDefined(HighlightArea))
        {
            HighlightArea = HighlightArea.FullScreen;
        }

        HighlightColor = HighlightColor;
    }
}

internal enum HighlightArea
{
    FullScreen,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

internal readonly record struct HighlightOptions(HighlightArea Area, Color Color);

internal sealed class IssueHighlightOverlay : Form
{
    private const int EdgeWidth = 1;
    private const int GlowSize = 4;
    private const int CornerGlowDepth = 24;
    private const byte MaxGlowAlpha = 255;
    private const byte MaxCornerGlowAlpha = 255;
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
    private IntPtr glowMemoryDc;
    private IntPtr glowBitmapHandle;
    private IntPtr glowPreviousObject;
    private Size renderedGlowSize = Size.Empty;
    private HighlightOptions options = new(HighlightArea.FullScreen, Color.Red);

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
            DisposeGlowResources();
        }
    }

    public void SetOptions(HighlightOptions newOptions)
    {
        if (options == newOptions)
        {
            return;
        }

        options = newOptions;
        DisposeGlowResources();

        if (Visible)
        {
            RenderGlow();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pulseTimer.Dispose();
            DisposeGlowResources();
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

        EnsureGlowResources(new Size(Width, Height));
        ApplyLayeredBitmap(GetPulseIntensity());
    }

    private void EnsureGlowResources(Size size)
    {
        if (glowMemoryDc != IntPtr.Zero && glowBitmapHandle != IntPtr.Zero && renderedGlowSize == size)
        {
            return;
        }

        DisposeGlowResources();

        // The glow shape is static; pulse frames only adjust SourceConstantAlpha to avoid full-screen bitmap churn.
        using var bitmap = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.Clear(Color.Transparent);
            DrawGlow(graphics, bitmap.Size, options);
        }

        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            glowMemoryDc = CreateCompatibleDC(screenDc);
            glowBitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
            glowPreviousObject = SelectObject(glowMemoryDc, glowBitmapHandle);
            renderedGlowSize = size;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void DisposeGlowResources()
    {
        if (glowMemoryDc != IntPtr.Zero)
        {
            SelectObject(glowMemoryDc, glowPreviousObject);
        }

        if (glowBitmapHandle != IntPtr.Zero)
        {
            DeleteObject(glowBitmapHandle);
        }

        if (glowMemoryDc != IntPtr.Zero)
        {
            DeleteDC(glowMemoryDc);
        }

        glowMemoryDc = IntPtr.Zero;
        glowBitmapHandle = IntPtr.Zero;
        glowPreviousObject = IntPtr.Zero;
        renderedGlowSize = Size.Empty;
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

    private static void DrawGlow(Graphics graphics, Size size, HighlightOptions options)
    {
        if (IsCornerArea(options.Area))
        {
            DrawCornerGlow(graphics, size, options);
            return;
        }

        var maxDepth = Math.Min(GlowSize, Math.Min(size.Width, size.Height) / 2);
        for (var offset = 0; offset < maxDepth; offset++)
        {
            var alpha = GetGlowAlpha(offset);
            using var brush = new SolidBrush(Color.FromArgb(alpha, options.Color));

            FillHighlightArea(graphics, brush, size, options.Area, offset, 1);
        }

        using var edgeBrush = new SolidBrush(Color.FromArgb(MaxGlowAlpha, options.Color));
        FillHighlightArea(graphics, edgeBrush, size, options.Area, 0, EdgeWidth);
    }

    private static void DrawCornerGlow(Graphics graphics, Size size, HighlightOptions options)
    {
        var maxDepth = Math.Min(CornerGlowDepth, Math.Min(size.Width, size.Height));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // Draw larger, softer triangles first and stack smaller brighter triangles toward the corner apex.
        for (var depth = maxDepth; depth > 0; depth--)
        {
            var alpha = GetCornerGlowAlpha(depth, maxDepth);
            using var brush = new SolidBrush(Color.FromArgb(alpha, options.Color));

            graphics.FillPolygon(brush, GetCornerTrianglePoints(size, options.Area, depth));
        }
    }

    private static bool IsCornerArea(HighlightArea area) =>
        area is HighlightArea.TopLeft
            or HighlightArea.TopRight
            or HighlightArea.BottomLeft
            or HighlightArea.BottomRight;

    private static byte GetCornerGlowAlpha(int depth, int maxDepth)
    {
        if (maxDepth <= 1)
        {
            return MaxCornerGlowAlpha;
        }

        var distance = (double)(depth - 1) / (maxDepth - 1);
        var intensity = 1d - distance;
        return ScaleAlpha(MaxCornerGlowAlpha * intensity * intensity);
    }

    private static Point[] GetCornerTrianglePoints(Size size, HighlightArea area, int depth) =>
        area switch
        {
            HighlightArea.TopLeft =>
            [
                new Point(0, 0),
                new Point(depth, 0),
                new Point(0, depth),
            ],
            HighlightArea.TopRight =>
            [
                new Point(size.Width - 1, 0),
                new Point(size.Width - depth - 1, 0),
                new Point(size.Width - 1, depth),
            ],
            HighlightArea.BottomLeft =>
            [
                new Point(0, size.Height - 1),
                new Point(depth, size.Height - 1),
                new Point(0, size.Height - depth - 1),
            ],
            HighlightArea.BottomRight =>
            [
                new Point(size.Width - 1, size.Height - 1),
                new Point(size.Width - depth - 1, size.Height - 1),
                new Point(size.Width - 1, size.Height - depth - 1),
            ],
            _ => [],
        };

    private static void FillHighlightArea(
        Graphics graphics,
        Brush brush,
        Size size,
        HighlightArea area,
        int offset,
        int thickness)
    {
        switch (area)
        {
            case HighlightArea.FullScreen:
                FillHighlightArea(graphics, brush, size, HighlightArea.Top, offset, thickness);
                FillHighlightArea(graphics, brush, size, HighlightArea.Bottom, offset, thickness);
                FillHighlightArea(graphics, brush, size, HighlightArea.Left, offset, thickness);
                FillHighlightArea(graphics, brush, size, HighlightArea.Right, offset, thickness);
                break;
            case HighlightArea.Top:
                graphics.FillRectangle(brush, 0, offset, size.Width, thickness);
                break;
            case HighlightArea.Bottom:
                graphics.FillRectangle(brush, 0, size.Height - offset - thickness, size.Width, thickness);
                break;
            case HighlightArea.Left:
                graphics.FillRectangle(brush, offset, 0, thickness, size.Height);
                break;
            case HighlightArea.Right:
                graphics.FillRectangle(brush, size.Width - offset - thickness, 0, thickness, size.Height);
                break;
        }
    }

    private static byte GetGlowAlpha(int offset)
    {
        var distance = Math.Max(0d, 1d - (double)offset / GlowSize);
        return ScaleAlpha(MaxGlowAlpha * distance * distance);
    }

    private static byte ScaleAlpha(double alpha) =>
        (byte)Math.Round(Math.Clamp(alpha, 0d, byte.MaxValue));

    private void ApplyLayeredBitmap(double pulseIntensity)
    {
        var screenDc = GetDC(IntPtr.Zero);

        try
        {
            var size = new NativeSize(renderedGlowSize.Width, renderedGlowSize.Height);
            var source = new NativePoint(0, 0);
            var destination = new NativePoint(Left, Top);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = ScaleAlpha(byte.MaxValue * pulseIntensity),
                AlphaFormat = AcSrcAlpha,
            };

            UpdateLayeredWindow(
                Handle,
                screenDc,
                ref destination,
                ref size,
                glowMemoryDc,
                ref source,
                0,
                ref blend,
                UlwAlpha);
        }
        finally
        {
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
