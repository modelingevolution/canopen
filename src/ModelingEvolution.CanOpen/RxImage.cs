namespace ModelingEvolution.CanOpen;

/// <summary>
/// A persistent, mutable RxPDO payload that <see cref="CanOpenClient"/> re-transmits every cycle
/// (the watchdog feed). Callers mutate bytes/bits; the client snapshots and sends on each tick.
/// Thread-safe.
/// </summary>
public sealed class RxImage
{
    private readonly byte[] _data;
    private readonly Lock _gate = new();

    public uint CobId { get; }
    public TimeSpan Period { get; }

    internal RxImage(uint cobId, int dlc, TimeSpan period)
    {
        if (dlc is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(dlc), "CAN 2.0 DLC must be 0..8");
        CobId = cobId;
        Period = period;
        _data = new byte[dlc];
    }

    public void Set(int offset, ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
            bytes.CopyTo(_data.AsSpan(offset));
    }

    public void SetByte(int offset, byte value)
    {
        lock (_gate)
            _data[offset] = value;
    }

    /// <summary>Writes a 16-bit value little-endian at <paramref name="offset"/>.</summary>
    public void SetU16(int offset, ushort value)
    {
        lock (_gate)
        {
            _data[offset] = (byte)(value & 0xFF);
            _data[offset + 1] = (byte)(value >> 8);
        }
    }

    public void SetBit(int byteOffset, int bit, bool on)
    {
        lock (_gate)
        {
            byte mask = (byte)(1 << bit);
            if (on) _data[byteOffset] |= mask;
            else _data[byteOffset] &= (byte)~mask;
        }
    }

    /// <summary>Returns a copy of the current payload for transmission.</summary>
    public byte[] Snapshot()
    {
        lock (_gate)
            return (byte[])_data.Clone();
    }
}
