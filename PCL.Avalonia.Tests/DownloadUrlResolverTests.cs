using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Tests;

public sealed class DownloadUrlResolverTests
{
    private readonly DownloadUrlResolver _resolver = new();

    [Fact]
    public void GetVersionManifestUrls_ReturnsSourceOrder()
    {
        Assert.Equal([DownloadUrlResolver.MojangManifestUrl], _resolver.GetVersionManifestUrls(DownloadSource.Mojang));

        var urls = _resolver.GetVersionManifestUrls(DownloadSource.Bmclapi);

        Assert.Equal(DownloadUrlResolver.BmclapiManifestUrl, urls[0]);
        Assert.Equal(DownloadUrlResolver.MojangManifestUrl, urls[1]);
    }

    [Fact]
    public void GetVersionJsonUrls_BmclapiUsesMirror()
    {
        var urls = _resolver.GetVersionJsonUrls(DownloadSource.Bmclapi, null, "1.20.1");

        Assert.Equal(["https://bmclapi2.bangbang93.com/version/1.20.1/json"], urls);
    }

    [Fact]
    public void GetVersionJsonUrls_MojangRequiresEntry()
    {
        var entry = new VersionManifestEntry { Id = "1.20.1", Url = "https://example.com/1.20.1.json" };

        Assert.Equal([entry.Url], _resolver.GetVersionJsonUrls(DownloadSource.Mojang, entry, "1.20.1"));
        Assert.Throws<InvalidOperationException>(
            () => _resolver.GetVersionJsonUrls(DownloadSource.Mojang, null, "1.20.1"));
    }

    [Fact]
    public void GetClientJarUrls_ResolvesMirrorAndOriginal()
    {
        var bmclapi = _resolver.GetClientJarUrls(DownloadSource.Bmclapi, "1.20.1", "https://example.com/client.jar");
        Assert.Equal("https://bmclapi2.bangbang93.com/version/1.20.1/jar", bmclapi[0]);
        Assert.Equal("https://example.com/client.jar", bmclapi[1]);

        Assert.Equal(
            ["https://example.com/client.jar"],
            _resolver.GetClientJarUrls(DownloadSource.Mojang, "1.20.1", "https://example.com/client.jar"));
        Assert.Throws<InvalidOperationException>(
            () => _resolver.GetClientJarUrls(DownloadSource.Mojang, "1.20.1", null));
    }

    [Fact]
    public void GetLibraryUrls_WithoutOriginalUrl_UsesOfficialRoot()
    {
        var urls = _resolver.GetLibraryUrls(DownloadSource.Mojang, null, "com/example/core/1.0/core-1.0.jar");

        Assert.Equal(["https://libraries.minecraft.net/com/example/core/1.0/core-1.0.jar"], urls);
    }

    [Fact]
    public void GetAssetUrls_ReturnsMirrorOrOfficial()
    {
        var mojang = _resolver.GetAssetUrls(DownloadSource.Mojang, "abcdef1234");
        Assert.Equal(["https://resources.download.minecraft.net/ab/abcdef1234"], mojang);

        var bmclapi = _resolver.GetAssetUrls(DownloadSource.Bmclapi, "abcdef1234");
        Assert.Equal("https://bmclapi2.bangbang93.com/assets/ab/abcdef1234", bmclapi[0]);
        Assert.Equal("https://resources.download.minecraft.net/ab/abcdef1234", bmclapi[1]);
    }
}
