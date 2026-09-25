using System.Text;

namespace PCL.Avalonia.Services.Link;

public static class LinkInviteCodec
{
    public const int InviteCodeVersion = 2;
    private const string LookalikeReplacements = "O0I1";

    public static string FixInviteCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "";
        }

        var result = code;
        var open = result.IndexOf('【');
        var close = result.IndexOf('】', open < 0 ? 0 : open + 1);
        if (open >= 0 && close > open)
        {
            result = result[(open + 1)..close];
        }
        else
        {
            open = result.IndexOf('[');
            close = result.IndexOf(']', open < 0 ? 0 : open + 1);
            if (open >= 0 && close > open)
            {
                result = result[(open + 1)..close];
            }
        }

        result = result.ToUpperInvariant();
        for (var index = 0; index < LookalikeReplacements.Length; index += 2)
        {
            result = result.Replace(
                LookalikeReplacements[index].ToString(),
                LookalikeReplacements[index + 1].ToString());
        }

        if (result.Length >= 17
            && (result.Length < 23 || (result.Length >= 18 && result[17] != '-')))
        {
            result = result[..17] + "-0105E";
        }

        return result.Trim();
    }

    public static LinkInviteInfo ParseInvite(string code)
    {
        code = FixInviteCode(code);
        var error = ValidateCode(code);
        if (error is not null)
        {
            throw new FormatException(error);
        }

        var serverPort = Convert.ToInt32(code.Substring(1, 4), 16);
        var networkName = code[..11];
        var networkSecret = code.Substring(12, 5);
        var discoverNodeId = code.Substring(20, 3) == "000"
            ? -2
            : Convert.ToInt32(code.Substring(20, 3), 16);
        return new LinkInviteInfo(networkName, networkSecret, serverPort, discoverNodeId);
    }

    public static string? ValidateCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "邀请码为空！";
        }

        var fixedCode = FixInviteCode(code);
        if (fixedCode.StartsWith("U/", StringComparison.OrdinalIgnoreCase))
        {
            return "请让房主使用 PCL 创建房间！";
        }

        if (fixedCode.Length == 10)
        {
            return "请让房主使用非社区版的 PCL 创建房间！";
        }

        if (!(fixedCode.Length >= 14
            && fixedCode[0] == 'P'
            && fixedCode[5] == '-'
            && fixedCode[11] == '-'))
        {
            return "邀请码有误，请让房主使用 PCL 创建房间！";
        }

        if (fixedCode.Length >= 23
            && fixedCode[17] == '-'
            && fixedCode.Substring(18, 2) is var versionText
            && int.TryParse(versionText, out var version)
            && version > InviteCodeVersion)
        {
            return "你的 PCL 版本太老了，请在更新 PCL 之后再联机！";
        }

        return null;
    }

    public static string GenerateInvite(LinkInviteInfo invite)
    {
        var networkName = invite.NetworkName;
        var networkSecret = invite.NetworkSecret;
        var nodeId = invite.DiscoverNodeId is -1 or -2 ? 0 : invite.DiscoverNodeId;
        return $"{networkName}-{networkSecret}-{InviteCodeVersion:00}{ConvertToRadix16(nodeId, 3)}";
    }

    private static string ConvertToRadix16(int value, int length)
    {
        return Convert.ToString(value, 16)?.ToUpperInvariant().PadLeft(length, '0') ?? "000";
    }

    public static string GenerateRandomCode(Random? random = null)
    {
        const string alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var source = random ?? Random.Shared;
        var builder = new StringBuilder(5);
        for (var index = 0; index < 5; index++)
        {
            builder.Append(alphabet[source.Next(alphabet.Length)]);
        }

        return builder.ToString();
    }
}
