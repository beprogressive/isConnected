namespace IsConnected;

internal static class NetworkSpeedFormatter
{
    private const decimal BytesPerMegabyte = 1_000_000m;

    public static string FormatMegabytesPerSecondValue(long bitsPerSecond)
    {
        var megabytesPerSecond = bitsPerSecond / 8m / BytesPerMegabyte;
        return $"{megabytesPerSecond:0.##}";
    }
}
