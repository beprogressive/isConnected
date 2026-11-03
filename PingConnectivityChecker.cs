using System.Net.NetworkInformation;

namespace IsConnected;

internal sealed class PingConnectivityChecker : IConnectivityChecker
{
    private const int PingTimeoutMs = 1_500;
    private const string PrimaryTargetHost = "8.8.8.8";
    private const string FallbackTargetHost = "1.1.1.1";

    public async Task<ConnectivityCheckResult> CheckAsync()
    {
        var primaryResult = await TryPingAsync(PrimaryTargetHost);
        if (primaryResult.Success)
        {
            return new ConnectivityCheckResult(true, primaryResult.RoundtripMs, PrimaryTargetHost);
        }

        var fallbackResult = await TryPingAsync(FallbackTargetHost);
        if (fallbackResult.Success)
        {
            return new ConnectivityCheckResult(true, fallbackResult.RoundtripMs, FallbackTargetHost);
        }

        return new ConnectivityCheckResult(false, null, null);
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
}
