using System.Globalization;

namespace ModelingEvolution.CanOpen;

/// <summary>
/// A CANopen object-dictionary address: a 16-bit Index and an 8-bit SubIndex.
/// The analog of a Modbus register address.
/// </summary>
public readonly record struct ObjectAddress(ushort Index, byte Sub)
{
    public override string ToString() =>
        $"0x{Index.ToString("X4", CultureInfo.InvariantCulture)}:{Sub.ToString("X2", CultureInfo.InvariantCulture)}";
}
