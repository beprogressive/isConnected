using System.Diagnostics;
using System.Net.NetworkInformation;

namespace IsConnected;

internal sealed class NetworkSpeedMonitor : IDisposable
{
    private const int SampleIntervalMs = 500;

    private readonly INetworkTrafficCounter trafficCounter;
    private readonly System.Windows.Forms.Timer timer;
    private NetworkTrafficSample previousSample;
    private uint primaryInterfaceIndex;
    private long previousTimestamp;
    private bool hasPreviousSample;
    private int resetRequested;

    public NetworkSpeedMonitor()
        : this(new WindowsNetworkTrafficCounter())
    {
    }

    private NetworkSpeedMonitor(INetworkTrafficCounter trafficCounter)
    {
        this.trafficCounter = trafficCounter;
        timer = new System.Windows.Forms.Timer { Interval = SampleIntervalMs };
        timer.Tick += (_, _) => PublishSample();

        NetworkChange.NetworkAddressChanged += HandleNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += HandleNetworkChanged;
    }

    public event EventHandler<NetworkSpeedSnapshot>? SpeedChanged;

    public void Start()
    {
        Interlocked.Exchange(ref resetRequested, 0);
        ResetBaseline();
        timer.Start();
    }

    public void Stop()
    {
        timer.Stop();
        Interlocked.Exchange(ref resetRequested, 0);
        hasPreviousSample = false;
        SpeedChanged?.Invoke(this, NetworkSpeedSnapshot.Unavailable);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= HandleNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= HandleNetworkChanged;
        timer.Dispose();
    }

    private void HandleNetworkChanged(object? sender, EventArgs args) =>
        Interlocked.Exchange(ref resetRequested, 1);

    private void ResetBaseline()
    {
        if (!trafficCounter.TryResolvePrimaryInterface(out primaryInterfaceIndex)
            || !trafficCounter.TryReadInterfaceSample(primaryInterfaceIndex, out previousSample))
        {
            primaryInterfaceIndex = 0;
            hasPreviousSample = false;
            SpeedChanged?.Invoke(this, NetworkSpeedSnapshot.Unavailable);
            return;
        }

        previousTimestamp = Stopwatch.GetTimestamp();
        hasPreviousSample = true;
        SpeedChanged?.Invoke(
            this,
            new NetworkSpeedSnapshot(0, 0, previousSample.InterfaceName, true));
    }

    private void PublishSample()
    {
        if (Interlocked.Exchange(ref resetRequested, 0) == 1)
        {
            ResetBaseline();
            return;
        }

        if (primaryInterfaceIndex == 0
            || !trafficCounter.TryReadInterfaceSample(primaryInterfaceIndex, out var currentSample))
        {
            ResetBaseline();
            return;
        }

        var currentTimestamp = Stopwatch.GetTimestamp();
        if (!hasPreviousSample || currentSample.InterfaceName != previousSample.InterfaceName)
        {
            previousSample = currentSample;
            previousTimestamp = currentTimestamp;
            hasPreviousSample = true;
            SpeedChanged?.Invoke(
                this,
                new NetworkSpeedSnapshot(0, 0, currentSample.InterfaceName, true));
            return;
        }

        var elapsedSeconds = (currentTimestamp - previousTimestamp) / (double)Stopwatch.Frequency;
        var receivedDelta = GetCounterDelta(previousSample.ReceivedBytes, currentSample.ReceivedBytes);
        var sentDelta = GetCounterDelta(previousSample.SentBytes, currentSample.SentBytes);

        previousSample = currentSample;
        previousTimestamp = currentTimestamp;

        if (elapsedSeconds <= 0 || receivedDelta is null || sentDelta is null)
        {
            SpeedChanged?.Invoke(
                this,
                new NetworkSpeedSnapshot(0, 0, currentSample.InterfaceName, true));
            return;
        }

        SpeedChanged?.Invoke(
            this,
            new NetworkSpeedSnapshot(
                ToBitsPerSecond(receivedDelta.Value, elapsedSeconds),
                ToBitsPerSecond(sentDelta.Value, elapsedSeconds),
                currentSample.InterfaceName,
                true));
    }

    private static ulong? GetCounterDelta(ulong previous, ulong current) =>
        current >= previous ? current - previous : null;

    private static long ToBitsPerSecond(ulong byteDelta, double elapsedSeconds) =>
        (long)Math.Round(byteDelta * 8d / elapsedSeconds);
}
