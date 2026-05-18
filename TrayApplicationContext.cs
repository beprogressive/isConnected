namespace IsConnected;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int InitialCheckDelayMs = 250;
    private const int MaximumJitterMs = 5_000;
    private const int JitterDivisor = 10;

    private readonly IConnectivityChecker connectivityChecker;
    private readonly AppSettingsStore settingsStore;
    private readonly TrayMenuHost trayMenuHost;
    private readonly NotifyIcon trayIcon;
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem statusItem;
    private readonly ToolStripMenuItem autostartItem;
    private readonly ToolStripMenuItem highlightIssueItem;
    private readonly ToolStripMenuItem showCurrentSpeedItem;
    private readonly ToolStripMenuItem speedOverlayCornerMenu;
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
    private readonly CurrentSpeedOverlay currentSpeedOverlay;
    private readonly NetworkSpeedMonitor networkSpeedMonitor;

    private bool checkInProgress;
    private bool isOnline;
    private bool isExiting;
    private bool suppressAutostartChange;
    private bool suppressHighlightIssueChange;
    private bool suppressShowCurrentSpeedChange;
    private bool issueTestActive;

    public TrayApplicationContext()
        : this(new PingConnectivityChecker(), new AppSettingsStore())
    {
    }

    private TrayApplicationContext(IConnectivityChecker connectivityChecker, AppSettingsStore settingsStore)
    {
        this.connectivityChecker = connectivityChecker;
        this.settingsStore = settingsStore;
        settings = settingsStore.Load();

        trayMenuHost = new TrayMenuHost();
        onlineIcon = TrayIconFactory.CreateAppIcon();
        offlineIcon = TrayIconFactory.CreateOfflineAppIcon();

        statusItem = new ToolStripMenuItem("Checking internet...") { Enabled = false };
        autostartItem = new ToolStripMenuItem("Autostart") { CheckOnClick = true };
        autostartItem.Checked = AutostartManager.IsEnabled();
        autostartItem.CheckedChanged += (_, _) => TrySetAutostart(autostartItem.Checked);

        highlightIssueItem = new ToolStripMenuItem("Highlight issue") { CheckOnClick = true };
        highlightIssueItem.Checked = settings.HighlightIssue;
        highlightIssueItem.CheckedChanged += (_, _) => TrySetHighlightIssue(highlightIssueItem.Checked);

        showCurrentSpeedItem = new ToolStripMenuItem("Show current speed") { CheckOnClick = true };
        showCurrentSpeedItem.Checked = settings.ShowCurrentSpeed;
        showCurrentSpeedItem.CheckedChanged += (_, _) => TrySetShowCurrentSpeed(showCurrentSpeedItem.Checked);

        speedOverlayCornerMenu = new ToolStripMenuItem("Speed overlay corner");
        RebuildSpeedOverlayCornerMenu();

        highlightAreaMenu = new ToolStripMenuItem("Highlight area");
        RebuildHighlightAreaMenu();

        highlightColorItem = new ToolStripMenuItem("Highlight color");
        highlightColorItem.Click += (_, _) => TrySetHighlightColor();
        UpdateHighlightColorPreview();

        intervalMenu = new ToolStripMenuItem("Ping interval");
        RebuildIntervalMenu();

        var checkNowItem = new ToolStripMenuItem("Check now");
        checkNowItem.Click += async (_, _) => await CheckNowAsync();

        testIssueItem = new ToolStripMenuItem("Test issue");
        testIssueItem.Click += (_, _) => StartIssueTest();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        var aboutItem = new ToolStripMenuItem("About");
        aboutItem.Click += (_, _) => ShowAboutDialog();

        menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autostartItem);
        menu.Items.Add(highlightIssueItem);
        menu.Items.Add(showCurrentSpeedItem);
        menu.Items.Add(speedOverlayCornerMenu);
        menu.Items.Add(highlightAreaMenu);
        menu.Items.Add(highlightColorItem);
        menu.Items.Add(intervalMenu);
        menu.Items.Add(testIssueItem);
        menu.Items.Add(checkNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(aboutItem);
        menu.Items.Add(exitItem);

        trayIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = offlineIcon,
            Text = "IsConnected: checking...",
            Visible = true,
        };
        trayIcon.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                trayMenuHost.ShowContextMenu(menu, Cursor.Position);
            }
        };

        issueHighlightOverlay = new IssueHighlightOverlay();
        currentSpeedOverlay = new CurrentSpeedOverlay();
        networkSpeedMonitor = new NetworkSpeedMonitor();
        networkSpeedMonitor.SpeedChanged += (_, speed) => UpdateCurrentSpeed(speed);
        ApplyHighlightOptions();
        ApplyCurrentSpeedVisibility();

        timer = new System.Windows.Forms.Timer { Interval = InitialCheckDelayMs };
        timer.Tick += async (_, _) => await RunScheduledCheckAsync();
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
            networkSpeedMonitor.Dispose();
            currentSpeedOverlay.Dispose();
            issueHighlightOverlay.Dispose();
            menu.Dispose();
            trayMenuHost.Dispose();
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
            SetStatus(await connectivityChecker.CheckAsync());
        }
        catch
        {
            SetStatus(new ConnectivityCheckResult(false, null, null));
        }
        finally
        {
            checkInProgress = false;
        }
    }

    private async Task RunScheduledCheckAsync()
    {
        if (isExiting)
        {
            return;
        }

        timer.Stop();

        try
        {
            await CheckConnectivityAsync();
        }
        finally
        {
            ScheduleNextCheck();
        }
    }

    private async Task CheckNowAsync()
    {
        if (checkInProgress)
        {
            return;
        }

        timer.Stop();

        try
        {
            await CheckConnectivityAsync();
        }
        finally
        {
            ScheduleNextCheck();
        }
    }

    private void SetStatus(ConnectivityCheckResult result)
    {
        if (isExiting)
        {
            return;
        }

        isOnline = result.IsOnline;
        trayIcon.Icon = isOnline ? onlineIcon : offlineIcon;

        var checkedAt = DateTime.Now.ToString("HH:mm:ss");
        var statusText = isOnline
            ? $"Online, {result.RoundtripMs} ms via {result.Host}"
            : "Offline";

        statusItem.Text = $"{statusText} (last check {checkedAt})";
        trayIcon.Text = $"IsConnected: {statusText}";
        UpdateIssueEffects();
    }

    private void RebuildIntervalMenu()
    {
        intervalMenu.DropDownItems.Clear();

        foreach (var seconds in PingIntervalOptions.All)
        {
            var item = new ToolStripMenuItem(FormatInterval(seconds))
            {
                CheckOnClick = true,
                Checked = settings.IntervalSeconds == seconds,
                Tag = seconds,
            };

            item.Click += (_, _) => TrySetPingInterval(seconds);
            intervalMenu.DropDownItems.Add(item);
        }
    }

    private void TrySetPingInterval(int seconds)
    {
        var previousIntervalSeconds = settings.IntervalSeconds;
        settings.IntervalSeconds = seconds;

        try
        {
            settingsStore.Save(settings);
            RestartCheckTimer();
            RebuildIntervalMenu();
        }
        catch (Exception ex)
        {
            settings.IntervalSeconds = previousIntervalSeconds;
            RestartCheckTimer();
            RebuildIntervalMenu();
            ShowError("Could not save ping interval", ex);
        }
    }

    private void RestartCheckTimer()
    {
        timer.Stop();

        if (!checkInProgress)
        {
            ScheduleNextCheck();
        }
    }

    private void ScheduleNextCheck()
    {
        if (isExiting)
        {
            return;
        }

        timer.Interval = GetNextCheckDelayMs();
        timer.Start();
    }

    private int GetNextCheckDelayMs()
    {
        var baseDelayMs = Math.Max(1, settings.IntervalSeconds) * 1_000;
        var jitterMs = Math.Min(baseDelayMs / JitterDivisor, MaximumJitterMs);

        if (jitterMs == 0)
        {
            return baseDelayMs;
        }

        // Spread checks across installed clients without changing the user's selected interval meaningfully.
        return baseDelayMs + Random.Shared.Next(-jitterMs, jitterMs + 1);
    }

    private static string FormatInterval(int seconds) =>
        seconds < 60
            ? $"{seconds} second{(seconds == 1 ? string.Empty : "s")}"
            : $"{seconds / 60} minutes";

    private void RebuildHighlightAreaMenu()
    {
        highlightAreaMenu.DropDownItems.Clear();

        foreach (var definition in HighlightAreaCatalog.All)
        {
            var item = new ToolStripMenuItem(definition.DisplayName)
            {
                CheckOnClick = true,
                Checked = settings.HighlightArea == definition.Area,
                Tag = definition.Area,
            };

            item.Click += (_, _) => TrySetHighlightArea(definition.Area);
            highlightAreaMenu.DropDownItems.Add(item);
        }
    }

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
            settingsStore.Save(settings);
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

    private void TrySetShowCurrentSpeed(bool enabled)
    {
        if (suppressShowCurrentSpeedChange)
        {
            return;
        }

        var previousValue = settings.ShowCurrentSpeed;
        settings.ShowCurrentSpeed = enabled;

        try
        {
            settingsStore.Save(settings);
            ApplyCurrentSpeedVisibility();
        }
        catch (Exception ex)
        {
            settings.ShowCurrentSpeed = previousValue;
            suppressShowCurrentSpeedChange = true;
            showCurrentSpeedItem.Checked = previousValue;
            suppressShowCurrentSpeedChange = false;
            ApplyCurrentSpeedVisibility();
            ShowError("Could not save current speed setting", ex);
        }
    }

    private void RebuildSpeedOverlayCornerMenu()
    {
        speedOverlayCornerMenu.DropDownItems.Clear();

        foreach (var definition in SpeedOverlayCornerCatalog.All)
        {
            var item = new ToolStripMenuItem(definition.DisplayName)
            {
                CheckOnClick = true,
                Checked = settings.SpeedOverlayCorner == definition.Corner,
                Tag = definition.Corner,
            };

            item.Click += (_, _) => TrySetSpeedOverlayCorner(definition.Corner);
            speedOverlayCornerMenu.DropDownItems.Add(item);
        }
    }

    private void TrySetHighlightArea(HighlightArea area)
    {
        var previousValue = settings.HighlightArea;
        settings.HighlightArea = area;

        try
        {
            settingsStore.Save(settings);
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
            settingsStore.Save(settings);
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

    private void TrySetSpeedOverlayCorner(SpeedOverlayCorner corner)
    {
        var previousValue = settings.SpeedOverlayCorner;
        settings.SpeedOverlayCorner = corner;

        try
        {
            settingsStore.Save(settings);
            ApplyCurrentSpeedOptions();
            RebuildSpeedOverlayCornerMenu();
        }
        catch (Exception ex)
        {
            settings.SpeedOverlayCorner = previousValue;
            ApplyCurrentSpeedOptions();
            RebuildSpeedOverlayCornerMenu();
            ShowError("Could not save speed overlay corner", ex);
        }
    }

    private void ApplyCurrentSpeedVisibility()
    {
        ApplyCurrentSpeedOptions();
        currentSpeedOverlay.SetVisible(settings.ShowCurrentSpeed && !isExiting);

        if (settings.ShowCurrentSpeed && !isExiting)
        {
            networkSpeedMonitor.Start();
            return;
        }

        networkSpeedMonitor.Stop();
    }

    private void ApplyCurrentSpeedOptions()
    {
        currentSpeedOverlay.SetCorner(settings.SpeedOverlayCorner);
    }

    private void UpdateCurrentSpeed(NetworkSpeedSnapshot speed)
    {
        if (isExiting || currentSpeedOverlay.IsDisposed)
        {
            return;
        }

        if (currentSpeedOverlay.InvokeRequired && currentSpeedOverlay.IsHandleCreated)
        {
            currentSpeedOverlay.BeginInvoke(() => currentSpeedOverlay.UpdateSpeed(speed));
            return;
        }

        currentSpeedOverlay.UpdateSpeed(speed);
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

    private static void ShowAboutDialog()
    {
        using var dialog = new AboutDialog();
        dialog.ShowDialog();
    }
}
