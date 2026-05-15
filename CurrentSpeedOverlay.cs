using Microsoft.Win32;

namespace IsConnected;

internal sealed class CurrentSpeedOverlay : Form
{
    private const int WmNchittest = 0x0084;
    private const int Httransparent = -1;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly Label label;
    private SpeedOverlayCorner corner = SpeedOverlayCorner.TopRight;

    public CurrentSpeedOverlay()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        Opacity = 0.72d;
        Padding = new Padding(8, 5, 8, 5);
        ShowIcon = false;
        ShowInTaskbar = false;
        Size = new Size(156, 48);
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        label = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            Font = SystemFonts.MessageBoxFont,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        Controls.Add(label);
        UpdateSpeed(NetworkSpeedSnapshot.Unavailable);
        SystemEvents.DisplaySettingsChanged += HandleDisplaySettingsChanged;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
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

        Hide();
    }

    public void SetCorner(SpeedOverlayCorner newCorner)
    {
        if (corner == newCorner)
        {
            return;
        }

        corner = newCorner;
        if (Visible)
        {
            PositionOnPrimaryScreen();
        }
    }

    public void UpdateSpeed(NetworkSpeedSnapshot speed)
    {
        label.Text = speed.IsAvailable
            ? $"↓ {NetworkSpeedFormatter.FormatBitsPerSecond(speed.DownloadBitsPerSecond)}{Environment.NewLine}↑ {NetworkSpeedFormatter.FormatBitsPerSecond(speed.UploadBitsPerSecond)}"
            : $"↓ --{Environment.NewLine}↑ --";
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= HandleDisplaySettingsChanged;
            label.Dispose();
        }

        base.Dispose(disposing);
    }

    private void HandleDisplaySettingsChanged(object? sender, EventArgs args)
    {
        if (Visible)
        {
            PositionOnPrimaryScreen();
        }
    }

    private void PositionOnPrimaryScreen()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        Location = corner switch
        {
            SpeedOverlayCorner.TopLeft => new Point(workingArea.Left + 12, workingArea.Top + 12),
            SpeedOverlayCorner.TopRight => new Point(workingArea.Right - Width - 12, workingArea.Top + 12),
            SpeedOverlayCorner.BottomLeft => new Point(workingArea.Left + 12, workingArea.Bottom - Height - 12),
            SpeedOverlayCorner.BottomRight => new Point(workingArea.Right - Width - 12, workingArea.Bottom - Height - 12),
            _ => new Point(workingArea.Right - Width - 12, workingArea.Top + 12),
        };
    }
}
