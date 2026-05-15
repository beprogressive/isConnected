namespace IsConnected;

internal interface IConnectivityChecker
{
    Task<ConnectivityCheckResult> CheckAsync();
}
