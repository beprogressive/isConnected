namespace IsConnected;

internal static class PingIntervalOptions
{
    public static readonly int[] All = [5, 10, 30, 60, 300];

    public static bool IsSupported(int seconds) => All.Contains(seconds);
}
