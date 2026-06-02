namespace ModelingEvolution.CanOpen;

/// <summary>
/// CANopen COB-ID layout and command constants (CiA 301). All COB-IDs are computed from a node id.
/// </summary>
public static class CanOpenDefs
{
    // NMT: broadcast on COB-ID 0x000, data = [command, nodeId].
    public const uint CobNmt = 0x000;
    public const byte NmtStart = 0x01;        // -> Operational
    public const byte NmtStop = 0x02;         // -> Stopped
    public const byte NmtPreOperational = 0x80;
    public const byte NmtResetNode = 0x81;
    public const byte NmtResetComm = 0x82;

    // SYNC / emergency / SDO / heartbeat base offsets (added to nodeId).
    public const uint EmcyBase = 0x080;
    public const uint TxPdo1Base = 0x180;
    public const uint RxPdo1Base = 0x200;
    public const uint TxPdo2Base = 0x280;
    public const uint RxPdo2Base = 0x300;
    public const uint TxPdo3Base = 0x380;
    public const uint RxPdo3Base = 0x400;
    public const uint TxPdo4Base = 0x480;
    public const uint RxPdo4Base = 0x500;
    public const uint SdoResponseBase = 0x580;  // server -> client
    public const uint SdoRequestBase = 0x600;   // client -> server
    public const uint HeartbeatBase = 0x700;    // also bootup (data = [0x00])

    // SDO command specifiers (expedited).
    public const byte SdoUploadRequest = 0x40;   // ccs=2 initiate upload (read)
    public const byte SdoAbort = 0x80;           // server abort response

    public static uint Emcy(int node) => EmcyBase + (uint)node;
    public static uint TxPdo(int node, int n) => (TxPdo1Base + (uint)(n - 1) * 0x100) + (uint)node;
    public static uint RxPdo(int node, int n) => (RxPdo1Base + (uint)(n - 1) * 0x100) + (uint)node;
    public static uint SdoRequest(int node) => SdoRequestBase + (uint)node;
    public static uint SdoResponse(int node) => SdoResponseBase + (uint)node;
    public static uint Heartbeat(int node) => HeartbeatBase + (uint)node;
    public static uint Bootup(int node) => HeartbeatBase + (uint)node;

    public static byte[] NmtFrame(byte command, int node) => new byte[] { command, (byte)node };

    /// <summary>The COB-ID a TxPDO mapping object index implies (0x1A00..0x1A03 → TxPDO1..4).</summary>
    public static uint TxPdoCobForMappingIndex(ushort mappingIndex, int node) =>
        TxPdo(node, mappingIndex - 0x1A00 + 1);

    /// <summary>The COB-ID an RxPDO mapping object index implies (0x1600..0x1603 → RxPDO1..4).</summary>
    public static uint RxPdoCobForMappingIndex(ushort mappingIndex, int node) =>
        RxPdo(node, mappingIndex - 0x1600 + 1);
}
