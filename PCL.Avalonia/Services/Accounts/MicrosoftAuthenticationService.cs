using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Avalonia.Services.Platform;

namespace PCL.Avalonia.Services.Accounts;

public sealed class MicrosoftAuthenticationService : IMicrosoftAuthenticationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string DeviceTokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string RefreshTokenUrl = "https://login.live.com/oauth20_token.srf";
    private const string XboxLiveUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string EntitlementsUrl = "https://api.minecraftservices.com/entitlements/mcstore";
    private const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
    private readonly HttpClient _httpClient;
    private readonly IBrowserLauncher _browserLauncher;
    private readonly string? _clientId;

    public MicrosoftAuthenticationService(
        IBrowserLauncher browserLauncher,
        HttpClient? httpClient = null,
        string? clientId = null)
    {
        ArgumentNullException.ThrowIfNull(browserLauncher);
        _browserLauncher = browserLauncher;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _clientId = clientId ?? Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID");
    }

    public async Task<MicrosoftAccountSession> LoginAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var clientId = ResolveClientId();
        try
        {
            Progress(progress, "正在申请微软设备代码…");
            var (deviceStatus, deviceBody) = await PostFormRawAsync(
                DeviceCodeUrl,
                CreateDeviceCodeForm(clientId),
                cancellationToken).ConfigureAwait(false);
            if (deviceStatus != HttpStatusCode.OK)
            {
                throw new InvalidOperationException(
                    "获取设备代码失败：" + ExtractErrorMessage(deviceBody));
            }

            var device = Deserialize<DeviceCodeResponse>(deviceBody);
            Progress(
                progress,
                $"请在浏览器打开 {device.VerificationUri} 并输入 {device.UserCode}");
            try
            {
                await _browserLauncher.OpenAsync(device.VerificationUri, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 浏览器无法打开时，用户仍可按界面提示手动打开。
            }

            var (oauthAccessToken, oauthRefreshToken) = await PollDeviceCodeAsync(
                clientId,
                device,
                progress,
                cancellationToken).ConfigureAwait(false);
            return await AcquireMinecraftSessionAsync(
                oauthAccessToken,
                oauthRefreshToken,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("无法连接微软服务器，请检查网络环境或使用加速器", ex);
        }
    }

    public async Task<MicrosoftAccountSession?> RefreshAsync(
        Account account,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var clientId = ResolveClientId();
        if (string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            return null;
        }

        try
        {
            Progress(progress, "正在刷新微软登录…");
            var (status, body) = await PostFormRawAsync(
                RefreshTokenUrl,
                new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["refresh_token"] = account.RefreshToken,
                    ["grant_type"] = "refresh_token",
                    ["scope"] = "XboxLive.signin offline_access",
                },
                cancellationToken,
                simulateBrowserHeaders: true).ConfigureAwait(false);
            if (status != HttpStatusCode.OK)
            {
                var text = body.ToLowerInvariant();
                if (text.Contains("expired", StringComparison.Ordinal)
                    || text.Contains("password expired", StringComparison.Ordinal)
                    || text.Contains("must sign in again", StringComparison.Ordinal)
                    || (text.Contains("refresh_token", StringComparison.Ordinal)
                        && text.Contains("is not valid", StringComparison.Ordinal)))
                {
                    return null;
                }

                throw new InvalidOperationException(
                    "刷新微软令牌失败：" + ExtractErrorMessage(body));
            }

            var token = Deserialize<TokenResponse>(body);
            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new InvalidOperationException("刷新微软令牌失败：响应中没有访问令牌");
            }

            var refreshToken = string.IsNullOrWhiteSpace(token.RefreshToken)
                ? account.RefreshToken
                : token.RefreshToken;
            return await AcquireMinecraftSessionAsync(
                token.AccessToken,
                refreshToken,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("无法连接微软服务器，请检查网络环境或使用加速器", ex);
        }
    }

    private async Task<(string AccessToken, string RefreshToken)> PollDeviceCodeAsync(
        string clientId,
        DeviceCodeResponse device,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var unknownFailures = 0;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(device.ExpiresIn, 900));
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (status, body) = await PostFormRawAsync(
                DeviceTokenUrl,
                new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = clientId,
                    ["device_code"] = device.DeviceCode,
                    ["scope"] = "XboxLive.signin offline_access",
                },
                cancellationToken).ConfigureAwait(false);
            if (status == HttpStatusCode.OK)
            {
                var token = Deserialize<TokenResponse>(body);
                if (!string.IsNullOrWhiteSpace(token.AccessToken))
                {
                    return (token.AccessToken, token.RefreshToken ?? "");
                }
            }

            var error = ExtractErrorMessage(body) ?? "";
            if (error.Contains("authorization_declined", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("你拒绝了启动器申请的登录权限");
            }

            if (error.Contains("expired_token", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("登录用时太长啦，重新试试吧");
            }

            if (error.Contains("Account security interrupt", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("该账号由于安全问题无法登录，请前往微软账户页获取更多信息");
            }

            if (error.Contains("service abuse", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("非常抱歉，该账号已被微软封禁，无法登录");
            }

            if (error.Contains("AADSTS70000", StringComparison.OrdinalIgnoreCase)
                || error.Contains("must sign in again", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("该账号需要在 Microsoft 账户中重新授权后才能登录");
            }

            if (error.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase))
            {
                Progress(progress, "等待你在浏览器中完成验证…");
            }
            else
            {
                unknownFailures++;
                if (unknownFailures > 3)
                {
                    throw new InvalidOperationException("登录轮询失败：" + error);
                }
            }

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(1, device.Interval - 1)),
                cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("登录用时太长啦，重新试试吧");
    }

    private async Task<MicrosoftAccountSession> AcquireMinecraftSessionAsync(
        string oauthAccessToken,
        string oauthRefreshToken,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Progress(progress, "正在连接 Xbox Live…");
        var xblBody = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Properties"] = new Dictionary<string, object>
            {
                ["AuthMethod"] = "RPS",
                ["SiteName"] = "user.auth.xboxlive.com",
                ["RpsTicket"] = "d=" + oauthAccessToken,
            },
            ["RelyingParty"] = "http://auth.xboxlive.com",
            ["TokenType"] = "JWT",
        });
        var (xblStatus, xblResponse) = await PostJsonRawAsync(
            XboxLiveUrl,
            xblBody,
            cancellationToken).ConfigureAwait(false);
        if (xblStatus != HttpStatusCode.OK)
        {
            throw new InvalidOperationException("Xbox Live 验证失败：" + ExtractErrorMessage(xblResponse));
        }

        var xbl = Deserialize<XboxLiveResponse>(xblResponse);
        Progress(progress, "正在验证 Xbox 身份…");
        var xstsBody = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Properties"] = new Dictionary<string, object>
            {
                ["SandboxId"] = "RETAIL",
                ["UserTokens"] = new[] { xbl.Token },
            },
            ["RelyingParty"] = "rp://api.minecraftservices.com/",
            ["TokenType"] = "JWT",
        });
        var (xstsStatus, xstsResponse) = await PostJsonRawAsync(
            XstsUrl,
            xstsBody,
            cancellationToken).ConfigureAwait(false);
        if (xstsStatus != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(MapXstsError(xstsResponse));
        }

        var xsts = Deserialize<XstsResponse>(xstsResponse);
        Progress(progress, "正在登录 Minecraft…");
        var minecraftBody = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["identityToken"] = $"XBL3.0 x={xsts.Uhs};{xsts.Token}",
        });
        var (minecraftStatus, minecraftResponse) = await PostJsonRawAsync(
            MinecraftLoginUrl,
            minecraftBody,
            cancellationToken).ConfigureAwait(false);
        if (minecraftStatus != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(MapMinecraftLoginError(minecraftStatus, minecraftResponse));
        }

        var minecraft = Deserialize<MinecraftLoginResponse>(minecraftResponse);
        Progress(progress, "正在验证正版资格…");
        var (entitlementsStatus, entitlementsBody) = await GetRawAsync(
            EntitlementsUrl,
            minecraft.AccessToken,
            cancellationToken).ConfigureAwait(false);
        if (entitlementsStatus != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                "验证正版资格失败：" + ExtractErrorMessage(entitlementsBody));
        }

        if (!HasGameEntitlement(entitlementsBody))
        {
            throw new InvalidOperationException("你尚未购买正版 Minecraft，或 Xbox Game Pass 已到期");
        }

        Progress(progress, "正在获取玩家档案…");
        var (profileStatus, profileBody) = await GetRawAsync(
            ProfileUrl,
            minecraft.AccessToken,
            cancellationToken).ConfigureAwait(false);
        if (profileStatus == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("请先创建 Minecraft 玩家档案，然后再重新登录");
        }

        if (profileStatus != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                "获取玩家档案失败：" + ExtractErrorMessage(profileBody));
        }

        var profile = Deserialize<MinecraftProfileResponse>(profileBody);
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("玩家档案不完整，请稍后重试");
        }

        var expiresIn = Math.Max(0, minecraft.ExpiresIn - 1200);
        return new MicrosoftAccountSession
        {
            Name = profile.Name,
            Uuid = profile.Id,
            AccessToken = minecraft.AccessToken,
            RefreshToken = oauthRefreshToken,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            SkinUrl = profile.Skins?.FirstOrDefault()?.Url,
            CapeUrl = profile.Capes?.FirstOrDefault()?.Url,
        };
    }

    private static Dictionary<string, string> CreateDeviceCodeForm(string clientId)
    {
        return new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["tenant"] = "/consumers",
            ["scope"] = "XboxLive.signin offline_access",
        };
    }

    private static bool HasGameEntitlement(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return items.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string MapXstsError(string body)
    {
        var errorCode = FindXErrCode(body);
        return errorCode switch
        {
            2148916227 => "该账号似乎已被微软封禁，无法登录",
            2148916233 => "你尚未注册 Xbox 账户，请在注册后再登录",
            2148916235 => "你的网络所在的国家或地区无法登录微软账号，请使用加速器或 VPN 后再试",
            2148916238 => "该账号年龄不足，需要先在 Microsoft 账户中修改出生日期",
            _ => "Xbox 身份验证失败：" + ExtractErrorMessage(body),
        };
    }

    private static string MapMinecraftLoginError(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            return "登录尝试太过频繁，请等待几分钟后再试";
        }

        if (status == HttpStatusCode.Forbidden)
        {
            return "当前 IP 的登录尝试异常。如果你使用了 VPN 或加速器，请关闭或更换节点后再试";
        }

        if (status == HttpStatusCode.ServiceUnavailable)
        {
            return "Mojang 服务器出现问题，请稍后再试";
        }

        var error = ExtractErrorMessage(body);
        if (error.Contains("ACCOUNT_SUSPENDED", StringComparison.OrdinalIgnoreCase))
        {
            return "该账号似乎已被微软封禁，无法登录";
        }

        return "Minecraft 登录失败：" + error;
    }

    private static long? FindXErrCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("XErr", out var value)
                && value.TryGetInt64(out var code))
            {
                return code;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private async Task<(HttpStatusCode Status, string Body)> PostFormRawAsync(
        string url,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken,
        bool simulateBrowserHeaders = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form),
        };
        if (simulateBrowserHeaders)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        }

        using var response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    private async Task<(HttpStatusCode Status, string Body)> PostJsonRawAsync(
        string url,
        string json,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        using var response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    private async Task<(HttpStatusCode Status, string Body)> GetRawAsync(
        string url,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (response.StatusCode, body);
    }

    private string ResolveClientId()
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            throw new InvalidOperationException(
                "未配置微软登录 ClientId。请像 PCL 一样设置环境变量 PCL_MS_CLIENT_ID 后重试");
        }

        return _clientId!;
    }

    private static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException("服务器返回了无法解析的数据");

    private static string ExtractErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, "error_description", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "errorMessage", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "message", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "reason", StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? body
                        : body;
                }
            }

            if (root.TryGetProperty("error", out var error))
            {
                return error.ValueKind == JsonValueKind.String
                    ? error.GetString() ?? body
                    : body;
            }
        }
        catch (JsonException)
        {
        }

        return body.Length <= 200 ? body : body[..200];
    }

    private static void Progress(IProgress<string>? progress, string message)
    {
        progress?.Report(message);
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PCL2.Avalonia", "1.0"));
        return client;
    }

    private sealed class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = "";

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = "";

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; set; } = "";

        [JsonPropertyName("interval")]
        public int Interval { get; set; } = 5;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 900;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class XboxLiveResponse
    {
        public string Token { get; set; } = "";

        public XboxDisplayClaims? DisplayClaims { get; set; }
    }

    private sealed class XstsResponse
    {
        public string Token { get; set; } = "";

        public XboxDisplayClaims? DisplayClaims { get; set; }

        public string Uhs => DisplayClaims?.Xui?.FirstOrDefault()?.Uhs ?? "";
    }

    private sealed class XboxDisplayClaims
    {
        public IReadOnlyList<XboxXui>? Xui { get; set; }
    }

    private sealed class XboxXui
    {
        public string Uhs { get; set; } = "";
    }

    private sealed class MinecraftLoginResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class MinecraftProfileResponse
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public IReadOnlyList<MinecraftAsset>? Skins { get; set; }

        public IReadOnlyList<MinecraftAsset>? Capes { get; set; }
    }

    private sealed class MinecraftAsset
    {
        public string Url { get; set; } = "";
    }
}
