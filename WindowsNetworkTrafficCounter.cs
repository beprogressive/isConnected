using System.Net;
using System.Runtime.InteropServices;

namespace IsConnected;

internal sealed class WindowsNetworkTrafficCounter : INetworkTrafficCounter
{
    private const uint NoError = 0;
    private const uint IfTypeSoftwareLoopback = 24;
    private static readonly IPAddress[] RouteProbeAddresses =
    [
        IPAddress.Parse("8.8.8.8"),
        IPAddress.Parse("1.1.1.1"),
    ];

    public bool TryResolvePrimaryInterface(out uint interfaceIndex)
    {
        interfaceIndex = 0;

        foreach (var address in RouteProbeAddresses)
        {
            if (!TryGetBestInterfaceIndex(address, out var candidateIndex))
            {
                continue;
            }

            if (!TryReadInterfaceSample(candidateIndex, out _))
            {
                continue;
            }

            interfaceIndex = candidateIndex;
            return true;
        }

        return false;
    }

    public bool TryReadInterfaceSample(uint interfaceIndex, out NetworkTrafficSample sample)
    {
        sample = default;

        var row = MibIfRow2.Create(interfaceIndex);
        var result = GetIfEntry2(ref row);
        if (result != NoError || row.OperStatus != IfOperStatus.Up || row.Type == IfTypeSoftwareLoopback)
        {
            return false;
        }

        sample = new NetworkTrafficSample(row.InOctets, row.OutOctets, row.Alias);
        return true;
    }

    private static bool TryGetBestInterfaceIndex(IPAddress address, out uint interfaceIndex)
    {
        var bytes = address.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        var result = GetBestInterface(BitConverter.ToUInt32(bytes), out interfaceIndex);
        return result == NoError;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetBestInterface(uint destinationAddress, out uint bestInterfaceIndex);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetIfEntry2(ref MibIfRow2 row);

    private enum IfOperStatus : uint
    {
        Up = 1,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MibIfRow2
    {
        private const int IfMaxStringSize = 256;
        private const int IfMaxPhysAddressLength = 32;

        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = IfMaxStringSize + 1)]
        public string Alias;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = IfMaxStringSize + 1)]
        public string Description;

        public uint PhysicalAddressLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = IfMaxPhysAddressLength)]
        public byte[] PhysicalAddress;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = IfMaxPhysAddressLength)]
        public byte[] PermanentPhysicalAddress;

        public uint Mtu;
        public uint Type;
        public uint TunnelType;
        public uint MediaType;
        public uint PhysicalMediumType;
        public uint AccessType;
        public uint DirectionType;
        public byte InterfaceAndOperStatusFlags;
        public IfOperStatus OperStatus;
        public uint AdminStatus;
        public uint MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;

        public static MibIfRow2 Create(uint interfaceIndex) =>
            new()
            {
                InterfaceIndex = interfaceIndex,
                Alias = string.Empty,
                Description = string.Empty,
                PhysicalAddress = new byte[IfMaxPhysAddressLength],
                PermanentPhysicalAddress = new byte[IfMaxPhysAddressLength],
            };
    }
}
