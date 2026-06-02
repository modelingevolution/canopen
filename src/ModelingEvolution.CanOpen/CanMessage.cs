namespace ModelingEvolution.CanOpen;

/// <summary>A received CAN 2.0 frame (standard 11-bit id), decoupled from the SocketCAN types.</summary>
public readonly record struct CanMessage(uint Id, byte[] Data)
{
    /// <summary>Byte 0 of the payload, or 0 if the frame is empty.</summary>
    public byte B0 => Data.Length > 0 ? Data[0] : (byte)0;
}
