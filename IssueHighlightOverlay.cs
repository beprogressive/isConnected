using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace IsConnected;

internal sealed class IssueHighlightOverlay : Form
{
    private const int EdgeWidth = 1;
    private const int GlowSize = 4;
    private const int CornerGlowDepth = 156;
    private const int CornerGlowSupersampleSteps = 3;
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
        var definition = HighlightAreaCatalog.Get(options.Area);
        if (definition.Corner is { } corner)
        {
            DrawCornerGlow(graphics, size, corner, options.Color);
            return;
        }

        var maxDepth = Math.Min(GlowSize, Math.Min(size.Width, size.Height) / 2);
        for (var offset = 0; offset < maxDepth; offset++)
        {
            var alpha = GetGlowAlpha(offset);
            using var brush = new SolidBrush(Color.FromArgb(alpha, options.Color));

            FillHighlightEdges(graphics, brush, size, definition.Edges, offset, 1);
        }

        using var edgeBrush = new SolidBrush(Color.FromArgb(MaxGlowAlpha, options.Color));
        FillHighlightEdges(graphics, edgeBrush, size, definition.Edges, 0, EdgeWidth);
    }

    private static void DrawCornerGlow(Graphics graphics, Size size, HighlightCorner corner, Color color)
    {
        var maxDepth = Math.Min(CornerGlowDepth, Math.Min(size.Width, size.Height));
        using var bitmap = new Bitmap(maxDepth, maxDepth, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

        for (var y = 0; y < maxDepth; y++)
        {
            for (var x = 0; x < maxDepth; x++)
            {
                var alpha = GetCornerPixelAlpha(corner, maxDepth, x, y);
                if (alpha > 0)
                {
                    bitmap.SetPixel(x, y, Color.FromArgb(alpha, color));
                }
            }
        }

        var apex = GetCornerBitmapApex(corner, maxDepth);
        bitmap.SetPixel(apex.X, apex.Y, Color.FromArgb(MaxCornerGlowAlpha, color));

        graphics.DrawImageUnscaled(bitmap, GetCornerBitmapLocation(size, corner, maxDepth));
    }

    private static byte GetCornerPixelAlpha(HighlightCorner corner, int depth, int x, int y)
    {
        var alphaTotal = 0d;
        var sampleCount = CornerGlowSupersampleSteps * CornerGlowSupersampleSteps;

        for (var sampleY = 0; sampleY < CornerGlowSupersampleSteps; sampleY++)
        {
            for (var sampleX = 0; sampleX < CornerGlowSupersampleSteps; sampleX++)
            {
                var px = x + (sampleX + 0.5d) / CornerGlowSupersampleSteps;
                var py = y + (sampleY + 0.5d) / CornerGlowSupersampleSteps;
                var (distanceX, distanceY) = GetCornerSampleDistances(corner, depth, px, py);
                var progress = (distanceX + distanceY) / depth;

                if (progress < 1d)
                {
                    var intensity = Math.Pow(1d - progress, 3.6d);
                    alphaTotal += MaxCornerGlowAlpha * intensity;
                }
            }
        }

        return ScaleAlpha(alphaTotal / sampleCount);
    }

    private static (double DistanceX, double DistanceY) GetCornerSampleDistances(
        HighlightCorner corner,
        int depth,
        double x,
        double y) =>
        corner switch
        {
            HighlightCorner.TopLeft => (x, y),
            HighlightCorner.TopRight => (depth - x, y),
            HighlightCorner.BottomLeft => (x, depth - y),
            HighlightCorner.BottomRight => (depth - x, depth - y),
            _ => (depth, depth),
        };

    private static Point GetCornerBitmapLocation(Size size, HighlightCorner corner, int depth) =>
        corner switch
        {
            HighlightCorner.TopLeft => new Point(0, 0),
            HighlightCorner.TopRight => new Point(size.Width - depth, 0),
            HighlightCorner.BottomLeft => new Point(0, size.Height - depth),
            HighlightCorner.BottomRight => new Point(size.Width - depth, size.Height - depth),
            _ => Point.Empty,
        };

    private static Point GetCornerBitmapApex(HighlightCorner corner, int depth) =>
        corner switch
        {
            HighlightCorner.TopLeft => new Point(0, 0),
            HighlightCorner.TopRight => new Point(depth - 1, 0),
            HighlightCorner.BottomLeft => new Point(0, depth - 1),
            HighlightCorner.BottomRight => new Point(depth - 1, depth - 1),
            _ => Point.Empty,
        };

    private static void FillHighlightEdges(
        Graphics graphics,
        Brush brush,
        Size size,
        IReadOnlyList<HighlightEdge> edges,
        int offset,
        int thickness)
    {
        foreach (var edge in edges)
        {
            FillHighlightEdge(graphics, brush, size, edge, offset, thickness);
        }
    }

    private static void FillHighlightEdge(
        Graphics graphics,
        Brush brush,
        Size size,
        HighlightEdge edge,
        int offset,
        int thickness)
    {
        switch (edge)
        {
            case HighlightEdge.Top:
                graphics.FillRectangle(brush, 0, offset, size.Width, thickness);
                break;
            case HighlightEdge.Bottom:
                graphics.FillRectangle(brush, 0, size.Height - offset - thickness, size.Width, thickness);
                break;
            case HighlightEdge.Left:
                graphics.FillRectangle(brush, offset, 0, thickness, size.Height);
                break;
            case HighlightEdge.Right:
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
