namespace IsConnected;

internal interface INetworkTrafficCounter
{
    bool TryResolvePrimaryInterface(out uint interfaceIndex);
    bool TryReadInterfaceSample(uint interfaceIndex, out NetworkTrafficSample sample);
}
