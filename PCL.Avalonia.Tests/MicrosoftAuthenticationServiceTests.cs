using System.Net;
using System.Text;
using System.Text.Json;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Platform;

namespace PCL.Avalonia.Tests;

public sealed class MicrosoftAuthenticationServiceTests
{
    private sealed class FakeBrowserLauncher : IBrowserLauncher
    {
        public List<string> OpenedUrls { get; } = [];

        public Task OpenAsync(string url, CancellationToken cancellationToken = default)
        {
            OpenedUrls.Add(url);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.ToString() ?? "");
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body)
        => new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"),
        };

    private static Func<HttpRequestMessage, HttpResponseMessage> CreateMinecraftResponder()
        => request =>
        {
            var url = request.RequestUri?.AbsoluteUri ?? "";
            if (url.Contains("user.auth.xboxlive.com", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    Token = "xbl-token",
                    DisplayClaims = new { xui = new[] { new { uhs = "xbl-uhs" } } },
                });
            }

            if (url.Contains("xsts.auth.xboxlive.com", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    Token = "xsts-token",
                    DisplayClaims = new { xui = new[] { new { uhs = "xsts-uhs" } } },
                });
            }

            if (url.Contains("login_with_xbox", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    access_token = "minecraft-token",
                    expires_in = 86400,
                });
            }

            if (url.Contains("entitlements/mcstore", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    items = new[] { new { name = "game_minecraft" } },
                });
            }

            if (url.Contains("minecraft/profile", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    id = "11111111-2222-3333-4444-555555555555",
                    name = "Alex",
                    skins = new[] { new { url = "https://example.com/skin.png" } },
                    capes = new[] { new { url = "https://example.com/cape.png" } },
                });
            }

            return JsonResponse(HttpStatusCode.NotFound, new { error = "unexpected " + url });
        };

    private static FakeHttpMessageHandler CreateLoginHandler()
        => new(request =>
        {
            var url = request.RequestUri?.AbsoluteUri ?? "";
            if (url.EndsWith("/devicecode", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    device_code = "device-code",
                    user_code = "USER-CODE",
                    verification_uri = "https://microsoft.com/devicelogin",
                    interval = 5,
                    expires_in = 900,
                });
            }

            if (url.EndsWith("/token", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    access_token = "oauth-token",
                    refresh_token = "oauth-refresh",
                    expires_in = 3600,
                });
            }

            return CreateMinecraftResponder()(request);
        });

    private static FakeHttpMessageHandler CreateRefreshHandler()
        => new(request =>
        {
            var url = request.RequestUri?.AbsoluteUri ?? "";
            if (url.Contains("login.live.com/oauth20_token.srf", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    access_token = "new-oauth-token",
                    refresh_token = "new-refresh",
                    expires_in = 3600,
                });
            }

            return CreateMinecraftResponder()(request);
        });

    [Fact]
    public async Task LoginAsync_FollowsDeviceCodeFlow_AndReturnsSession()
    {
        var browser = new FakeBrowserLauncher();
        var handler = CreateLoginHandler();
        var service = new MicrosoftAuthenticationService(
            browser,
            new HttpClient(handler),
            clientId: "test-client");

        var session = await service.LoginAsync();

        Assert.Equal("https://microsoft.com/devicelogin", Assert.Single(browser.OpenedUrls));
        Assert.Equal("Alex", session.Name);
        Assert.Equal("11111111-2222-3333-4444-555555555555", session.Uuid);
        Assert.Equal("minecraft-token", session.AccessToken);
        Assert.Equal("oauth-refresh", session.RefreshToken);
        Assert.Equal("https://example.com/skin.png", session.SkinUrl);
        Assert.Equal("https://example.com/cape.png", session.CapeUrl);
        Assert.Equal(7, handler.RequestUris.Count);
    }

    [Fact]
    public async Task LoginAsync_MapsXstsAgeError_ToChineseMessage()
    {
        var browser = new FakeBrowserLauncher();
        var handler = new FakeHttpMessageHandler(request =>
        {
            var url = request.RequestUri?.AbsoluteUri ?? "";
            if (url.EndsWith("/devicecode", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    device_code = "device-code",
                    user_code = "USER-CODE",
                    verification_uri = "https://microsoft.com/devicelogin",
                    interval = 5,
                    expires_in = 900,
                });
            }

            if (url.EndsWith("/token", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    access_token = "oauth-token",
                    refresh_token = "oauth-refresh",
                    expires_in = 3600,
                });
            }

            if (url.Contains("xsts.auth.xboxlive.com", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.BadRequest, new { XErr = 2148916238 });
            }

            return CreateMinecraftResponder()(request);
        });
        var service = new MicrosoftAuthenticationService(
            browser,
            new HttpClient(handler),
            clientId: "test-client");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

        Assert.Contains("出生日期", exception.Message);
    }

    [Fact]
    public async Task RefreshAsync_MissingRefreshToken_ReturnsNull()
    {
        var service = new MicrosoftAuthenticationService(
            new FakeBrowserLauncher(),
            new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            clientId: "test-client");

        var result = await service.RefreshAsync(new Account
        {
            Id = Guid.NewGuid(),
            Name = "Alex",
            Type = "microsoft",
            AccessToken = "token",
        });

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_UsesMicrosoftEndpoints_AndReturnsUpdatedSession()
    {
        var browser = new FakeBrowserLauncher();
        var handler = CreateRefreshHandler();
        var service = new MicrosoftAuthenticationService(
            browser,
            new HttpClient(handler),
            clientId: "test-client");

        var session = await service.RefreshAsync(new Account
        {
            Id = Guid.NewGuid(),
            Name = "Alex",
            Type = "microsoft",
            Uuid = "11111111-2222-3333-4444-555555555555",
            AccessToken = "old-token",
            RefreshToken = "old-refresh",
        });

        Assert.NotNull(session);
        Assert.Equal("minecraft-token", session!.AccessToken);
        Assert.Equal("new-refresh", session.RefreshToken);
        Assert.Contains("login.live.com/oauth20_token.srf", handler.RequestUris[0]);
    }

    [Fact]
    public async Task LoginAsync_MissingClientId_ThrowsChineseHint()
    {
        var service = new MicrosoftAuthenticationService(
            new FakeBrowserLauncher(),
            new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            clientId: "");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync());

        Assert.Contains("PCL_MS_CLIENT_ID", exception.Message);
    }
}
