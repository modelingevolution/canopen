using FluentAssertions;
using ModelingEvolution.CanOpen;
using Xunit;

namespace ModelingEvolution.CanOpen.Tests;

public class SdoTests
{
    [Fact]
    public void BuildUploadRequest_encodes_index_sub_little_endian()
    {
        var req = Sdo.BuildUploadRequest(new ObjectAddress(0x6411, 0x02));
        req.Should().Equal(new byte[] { 0x40, 0x11, 0x64, 0x02, 0, 0, 0, 0 });
    }

    [Theory]
    [InlineData(1, 0x2F)] // n=3 -> cs = 0x23 | (3<<2) = 0x2F
    [InlineData(2, 0x2B)] // n=2 -> 0x23 | (2<<2) = 0x2B
    [InlineData(4, 0x23)] // n=0 -> 0x23
    public void BuildDownloadRequest_sets_command_specifier_by_size(int len, byte expectedCs)
    {
        var value = new byte[len];
        var req = Sdo.BuildDownloadRequest(new ObjectAddress(0x6200, 1), value);
        req[0].Should().Be(expectedCs);
        req[1].Should().Be(0x00);
        req[2].Should().Be(0x62);
        req[3].Should().Be(0x01);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void BuildDownloadRequest_rejects_non_expedited_sizes(int len) =>
        ((Action)(() => Sdo.BuildDownloadRequest(new ObjectAddress(0x6200, 1), new byte[len])))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void TryParseUploadResponse_reads_expedited_2byte_value()
    {
        // BK5120 identity-style 2-byte expedited response (cs = 0x4B: e=1,s=1,n=2 -> 2 data bytes).
        var frame = new byte[] { 0x4B, 0x11, 0x64, 0x02, 0x1C, 0x1A, 0x00, 0x00 };
        Sdo.TryParseUploadResponse(frame, new ObjectAddress(0x6411, 0x02), out var data).Should().BeTrue();
        data.Should().Equal(new byte[] { 0x1C, 0x1A });
    }

    [Fact]
    public void TryParseUploadResponse_rejects_abort()
    {
        var frame = new byte[] { 0x80, 0x11, 0x64, 0x02, 0x00, 0x00, 0x02, 0x06 };
        Sdo.TryParseUploadResponse(frame, new ObjectAddress(0x6411, 0x02), out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseUploadResponse_rejects_address_mismatch()
    {
        var frame = new byte[] { 0x4B, 0x00, 0x60, 0x01, 0x40, 0x00, 0x00, 0x00 };
        Sdo.TryParseUploadResponse(frame, new ObjectAddress(0x6411, 0x02), out _).Should().BeFalse();
    }

    [Fact]
    public void IsDownloadAck_matches_confirmation_for_address()
    {
        var frame = new byte[] { 0x60, 0x11, 0x64, 0x02, 0, 0, 0, 0 };
        Sdo.IsDownloadAck(frame, new ObjectAddress(0x6411, 0x02)).Should().BeTrue();
        Sdo.IsDownloadAck(frame, new ObjectAddress(0x6411, 0x03)).Should().BeFalse();
    }
}
