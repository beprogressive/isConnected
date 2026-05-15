namespace IsConnected;

internal readonly record struct NetworkSpeedSnapshot(
    long DownloadBitsPerSecond,
    long UploadBitsPerSecond,
    string InterfaceName,
    bool IsAvailable)
{
    public static NetworkSpeedSnapshot Unavailable { get; } = new(0, 0, string.Empty, false);
}
