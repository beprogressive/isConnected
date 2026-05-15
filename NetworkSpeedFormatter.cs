namespace IsConnected;

internal static class NetworkSpeedFormatter
{
    private const decimal Kilo = 1_000m;
    private const decimal Mega = Kilo * Kilo;
    private const decimal Giga = Mega * Kilo;

    public static string FormatBitsPerSecond(long bitsPerSecond)
    {
        if (bitsPerSecond < 1_000)
        {
            return "0 bit/s";
        }

        if (bitsPerSecond < 1_000_000)
        {
            return $"{Math.Round(bitsPerSecond / Kilo)} Kbit/s";
        }

        if (bitsPerSecond < 1_000_000_000)
        {
            return $"{bitsPerSecond / Mega:0.#} Mbit/s";
        }

        return $"{bitsPerSecond / Giga:0.#} Gbit/s";
    }
}
