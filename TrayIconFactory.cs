using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;

namespace IsConnected;

internal static class TrayIconFactory
{
    private const string AppIconResourceName = "Assets.app.ico";
    private const int TrayIconSize = 64;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon CreateAppIcon() => LoadAppIcon();

    public static Icon CreateOfflineAppIcon()
    {
        using var appIcon = LoadAppIcon();
        using var bitmap = appIcon.ToBitmap();
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var slashBack = new Pen(Color.White, 12)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var slash = new Pen(Color.FromArgb(220, 53, 69), 7)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        graphics.DrawLine(slashBack, 12, 12, 52, 52);
        graphics.DrawLine(slash, 12, 12, 52, 52);

        return CreateIconFromBitmap(bitmap);
    }

    private static Icon LoadAppIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(AppIconResourceName)
            ?? throw new InvalidOperationException($"Embedded app icon '{AppIconResourceName}' was not found.");

        return new Icon(stream, TrayIconSize, TrayIconSize);
    }

    private static Icon CreateIconFromBitmap(Bitmap bitmap)
    {
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
