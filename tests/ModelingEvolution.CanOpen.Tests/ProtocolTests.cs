using FluentAssertions;
using ModelingEvolution.CanOpen;
using Xunit;

namespace ModelingEvolution.CanOpen.Tests;

public class CanOpenDefsTests
{
    [Fact]
    public void CobIds_for_node5_match_the_bench_proven_layout()
    {
        const int n = 5;
        CanOpenDefs.RxPdo(n, 1).Should().Be(0x205);
        CanOpenDefs.RxPdo(n, 2).Should().Be(0x305);
        CanOpenDefs.RxPdo(n, 3).Should().Be(0x405);
        CanOpenDefs.TxPdo(n, 1).Should().Be(0x185);
        CanOpenDefs.TxPdo(n, 2).Should().Be(0x285);
        CanOpenDefs.TxPdo(n, 3).Should().Be(0x385);
        CanOpenDefs.SdoRequest(n).Should().Be(0x605);
        CanOpenDefs.SdoResponse(n).Should().Be(0x585);
        CanOpenDefs.Bootup(n).Should().Be(0x705);
        CanOpenDefs.Emcy(n).Should().Be(0x085);
    }

    [Fact]
    public void NmtFrame_is_command_then_node()
    {
        CanOpenDefs.NmtFrame(CanOpenDefs.NmtStart, 5).Should().Equal(new byte[] { 0x01, 0x05 });
        CanOpenDefs.NmtFrame(CanOpenDefs.NmtPreOperational, 5).Should().Equal(new byte[] { 0x80, 0x05 });
    }

    [Fact]
    public void Mapping_index_resolves_to_the_right_pdo_cobid()
    {
        CanOpenDefs.TxPdoCobForMappingIndex(0x1A00, 5).Should().Be(0x185);
        CanOpenDefs.TxPdoCobForMappingIndex(0x1A02, 5).Should().Be(0x385);
        CanOpenDefs.RxPdoCobForMappingIndex(0x1600, 5).Should().Be(0x205);
    }
}

public class ObjectAddressTests
{
    [Fact]
    public void ToString_is_index_colon_sub_hex() =>
        new ObjectAddress(0x6411, 0x02).ToString().Should().Be("0x6411:02");
}

public class PdoMapTests
{
    [Fact]
    public void DecodeEntry_unpacks_index_sub_bitlength()
    {
        // 0x6000:01, 8 bits  ->  0x60000108
        var (addr, bits) = PdoMap.DecodeEntry(0x60000108);
        addr.Should().Be(new ObjectAddress(0x6000, 0x01));
        bits.Should().Be(8);

        // 0x6401:02, 16 bits ->  0x64010210
        var (addr2, bits2) = PdoMap.DecodeEntry(0x64010210);
        addr2.Should().Be(new ObjectAddress(0x6401, 0x02));
        bits2.Should().Be(16);
    }

    [Fact]
    public void Add_then_TryGet_roundtrips()
    {
        var map = new PdoMap();
        var e = new PdoMapEntry(new ObjectAddress(0x6401, 2), 0x285, ByteOffset: 2, BitLength: 16);
        map.Add(e);
        map.TryGet(new ObjectAddress(0x6401, 2), out var got).Should().BeTrue();
        got.Should().Be(e);
        map.TryGet(new ObjectAddress(0x6401, 3), out _).Should().BeFalse();
    }
}

public class RxImageTests
{
    private static RxImage New(int dlc = 4) =>
        new(0x305, dlc, TimeSpan.FromMilliseconds(100)); // internal ctor visible via InternalsVisibleTo

    [Fact]
    public void SetU16_writes_little_endian()
    {
        var img = New(8);
        img.SetU16(2, 0x8000);
        img.Snapshot().Should().Equal(new byte[] { 0, 0, 0x00, 0x80, 0, 0, 0, 0 });
    }

    [Fact]
    public void SetBit_sets_and_clears()
    {
        var img = New(4);
        img.SetBit(0, 1, true);   // Robot-ready style bit 2H
        img.Snapshot()[0].Should().Be(0x02);
        img.SetBit(0, 1, false);
        img.Snapshot()[0].Should().Be(0x00);
    }

    [Fact]
    public void Snapshot_is_a_copy()
    {
        var img = New(4);
        var snap = img.Snapshot();
        snap[0] = 0xFF;
        img.Snapshot()[0].Should().Be(0x00);
    }

    [Fact]
    public void Ctor_rejects_invalid_dlc() =>
        ((Action)(() => New(9))).Should().Throw<ArgumentOutOfRangeException>();
}
