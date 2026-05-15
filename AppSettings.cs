using System.Text.Json.Serialization;

namespace IsConnected;

internal sealed class AppSettings
{
    public const int DefaultIntervalSeconds = 30;

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

    public void Normalize()
    {
        if (!PingIntervalOptions.IsSupported(IntervalSeconds))
        {
            IntervalSeconds = DefaultIntervalSeconds;
        }

        if (!HighlightAreaCatalog.IsSupported(HighlightArea))
        {
            HighlightArea = HighlightArea.FullScreen;
        }

        HighlightColor = HighlightColor;
    }
}
