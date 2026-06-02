namespace ModelingEvolution.CanOpen;

/// <summary>
/// Pure (de)serialization of expedited SDO frames (CiA 301 §7.2.4). No I/O — unit-testable.
/// Only expedited transfers (≤4 data bytes) are supported, which covers every object the
/// integrations in this org touch.
/// </summary>
public static class Sdo
{
    /// <summary>Builds the 8-byte expedited upload (read) request for an object address.</summary>
    public static byte[] BuildUploadRequest(ObjectAddress a) =>
        new byte[] { CanOpenDefs.SdoUploadRequest, (byte)(a.Index & 0xFF), (byte)(a.Index >> 8), a.Sub, 0, 0, 0, 0 };

    /// <summary>Builds the 8-byte expedited download (write) request for 1..4 bytes.</summary>
    public static byte[] BuildDownloadRequest(ObjectAddress a, ReadOnlySpan<byte> value)
    {
        if (value.Length is < 1 or > 4)
            throw new ArgumentException("expedited download supports 1..4 bytes", nameof(value));

        int n = 4 - value.Length;                       // unused trailing bytes
        byte cs = (byte)(0x23 | (n << 2));              // ccs=1 (download), e=1, s=1
        var data = new byte[8];
        data[0] = cs;
        data[1] = (byte)(a.Index & 0xFF);
        data[2] = (byte)(a.Index >> 8);
        data[3] = a.Sub;
        value.CopyTo(data.AsSpan(4));
        return data;
    }

    /// <summary>True if the frame is the SDO upload response for <paramref name="a"/>; out the expedited data bytes.</summary>
    public static bool TryParseUploadResponse(ReadOnlySpan<byte> frame, ObjectAddress a, out byte[] data)
    {
        data = [];
        if (frame.Length < 8)
            return false;
        if (frame[1] != (byte)(a.Index & 0xFF) || frame[2] != (byte)(a.Index >> 8) || frame[3] != a.Sub)
            return false;

        byte cs = frame[0];
        if (cs == CanOpenDefs.SdoAbort)
            return false;

        int n = (cs >> 2) & 0x03;                       // bytes NOT containing data (when s=1)
        int size = (cs & 0x01) != 0 ? 4 - n : 4;        // s-bit set → size known
        data = frame.Slice(4, size).ToArray();
        return true;
    }

    /// <summary>True if the frame is a successful download confirmation (cs == 0x60) for <paramref name="a"/>.</summary>
    public static bool IsDownloadAck(ReadOnlySpan<byte> frame, ObjectAddress a) =>
        frame.Length >= 8 &&
        frame[0] == 0x60 &&
        frame[1] == (byte)(a.Index & 0xFF) && frame[2] == (byte)(a.Index >> 8) && frame[3] == a.Sub;
}
