namespace IsConnected;

internal readonly record struct ConnectivityCheckResult(
    bool IsOnline,
    long? RoundtripMs,
    string? Host);
