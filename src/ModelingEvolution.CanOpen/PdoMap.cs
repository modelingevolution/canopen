namespace ModelingEvolution.CanOpen;

/// <summary>One mapped object inside a PDO: where in the frame it lives and how wide it is.</summary>
public readonly record struct PdoMapEntry(ObjectAddress Address, uint CobId, int ByteOffset, int BitLength);

/// <summary>
/// The device's TxPDO mapping (which object-dictionary entry is carried by which PDO, at what
/// byte offset). Built from the mapping objects 0x1A00..0x1A03 read over SDO at start-up.
/// </summary>
public sealed class PdoMap
{
    private readonly Dictionary<ObjectAddress, PdoMapEntry> _byObject = new();

    public bool TryGet(ObjectAddress a, out PdoMapEntry entry) => _byObject.TryGetValue(a, out entry);

    public IReadOnlyCollection<PdoMapEntry> Entries => _byObject.Values;

    public void Add(PdoMapEntry e) => _byObject[e.Address] = e;

    /// <summary>
    /// Decodes a packed 32-bit PDO mapping entry (CiA 301): bits 31..16 = index, 15..8 = subindex,
    /// 7..0 = length in bits.
    /// </summary>
    public static (ObjectAddress Address, int BitLength) DecodeEntry(uint raw)
    {
        ushort index = (ushort)((raw >> 16) & 0xFFFF);
        byte sub = (byte)((raw >> 8) & 0xFF);
        int bitLength = (int)(raw & 0xFF);
        return (new ObjectAddress(index, sub), bitLength);
    }
}
