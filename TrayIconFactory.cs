using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace IsConnected;

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
            LineJoin = LineJoin.Round,
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
                EndCap = LineCap.Round,
            };
            using var slash = new Pen(color, 7)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
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
