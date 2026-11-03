namespace IsConnected;

internal readonly record struct NetworkTrafficSample(
    ulong ReceivedBytes,
    ulong SentBytes,
    string InterfaceName);
