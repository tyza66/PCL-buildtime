using PCL.Avalonia.Services.Link;

namespace PCL.Avalonia.Tests;

public sealed class LinkInviteCodecTests
{
    [Theory]
    [InlineData("P0001-ABCDE-12345-02000", 1, "12345", -2)]
    [InlineData("P2FA8-XYZ12-QWERT-02ABC", 0x2fa8, "QWERT", 0xabc)]
    [InlineData("P0FFF-12345-ZXCVB-02000", 0x0fff, "ZXCVB", -2)]
    public void Parse_RoundTripsInvite(string code, int port, string secret, int nodeId)
    {
        var invite = LinkInviteCodec.ParseInvite(code);

        Assert.Equal(port, invite.ServerPort);
        Assert.Equal(secret, invite.NetworkSecret);
        Assert.Equal(nodeId, invite.DiscoverNodeId);
        Assert.Equal(code, LinkInviteCodec.GenerateInvite(invite));
    }

    [Fact]
    public void Parse_NormalizesBracketsCaseAndLookalikeCharacters()
    {
        var invite = LinkInviteCodec.ParseInvite("【p0001-abcde-12345-o2000】");

        Assert.Equal(1, invite.ServerPort);
        Assert.Equal("12345", invite.NetworkSecret);
        Assert.Equal(-2, invite.DiscoverNodeId);
    }

    [Fact]
    public void Parse_SupportsVersionOneCode_ByAppendingVersionTwoSuffix()
    {
        var invite = LinkInviteCodec.ParseInvite("P0001-ABCDE-01010");

        Assert.Equal(1, invite.ServerPort);
        Assert.Equal("01010", invite.NetworkSecret);
        Assert.Equal(0x5e, invite.DiscoverNodeId);
    }

    [Fact]
    public void Validate_RejectsEmptyCode()
    {
        Assert.Contains("空", LinkInviteCodec.ValidateCode(""));
    }

    [Fact]
    public void Validate_RejectsHmclAndOldCeFormats()
    {
        Assert.Contains("PCL 创建房间", LinkInviteCodec.ValidateCode("Uabc12345678901"));
        Assert.Contains("非社区版", LinkInviteCodec.ValidateCode("1234567890"));
    }

    [Fact]
    public void Validate_RejectsNewerInviteVersion()
    {
        var code = LinkInviteCodec.GenerateInvite(new LinkInviteInfo("P0001-ABCDE", "ABCDE", 1, 0));
        var newer = code.Remove(18, 2).Insert(18, "99");

        Assert.Contains("版本", LinkInviteCodec.ValidateCode(newer));
    }

    [Fact]
    public void Validate_AcceptsValidCode()
    {
        var code = LinkInviteCodec.GenerateInvite(new LinkInviteInfo("P0001-ABCDE", "12345", 1, 7));

        Assert.Null(LinkInviteCodec.ValidateCode(code));
    }

    [Fact]
    public void Generate_HostCodeUsesZeroNodeId()
    {
        var code = LinkInviteCodec.GenerateInvite(new LinkInviteInfo("P0ABC-12345", "67890", 0xabc, -1));

        Assert.Equal("P0ABC-12345-67890-02000", code);
    }
}
