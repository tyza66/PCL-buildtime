using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class ModsFolderResolverTests
{

    private static string ModsFolder(string gameFolder) => Path.Combine(gameFolder, "mods");
    private static MinecraftVersion Version(string id, LoaderKind loader = LoaderKind.None)
        => new()
        {
            Id = id,
            Folder = $"/games/mc/versions/{id}",
            JsonPath = $"/games/mc/versions/{id}/{id}.json",
            Loader = loader,
        };

    [Fact]
    public void Resolve_IsolatedVersion_PointsToVersionModsFolder()
    {
        var resolved = ModsFolderResolver.Resolve(
            "/games/mc",
            Version("1.20.1-fabric"),
            VersionIsolationDefault.All);

        Assert.True(resolved.Isolated);
        Assert.Equal("/games/mc/versions/1.20.1-fabric", resolved.GameFolder);
        Assert.Equal(ModsFolder("/games/mc/versions/1.20.1-fabric"), resolved.ModsFolder);
    }

    [Fact]
    public void Resolve_IsolationOff_UsesSharedModsFolder()
    {
        var resolved = ModsFolderResolver.Resolve(
            "/games/mc",
            Version("1.20.1-fabric", LoaderKind.Fabric),
            VersionIsolationDefault.Off);

        Assert.False(resolved.Isolated);
        Assert.Equal("/games/mc", resolved.GameFolder);
        Assert.Equal(ModsFolder("/games/mc"), resolved.ModsFolder);
    }

    [Fact]
    public void Resolve_ManualOverride_WinsOverGlobalDefault()
    {
        var version = Version("1.20.1-fabric", LoaderKind.Fabric);

        var off = ModsFolderResolver.Resolve(
            "/games/mc",
            version,
            VersionIsolationDefault.All,
            new VersionSettings { Independent = false });
        Assert.False(off.Isolated);
        Assert.Equal(ModsFolder("/games/mc"), off.ModsFolder);

        var on = ModsFolderResolver.Resolve(
            "/games/mc",
            Version("1.20.1"),
            VersionIsolationDefault.Off,
            new VersionSettings { Independent = true });
        Assert.True(on.Isolated);
        Assert.Equal(ModsFolder("/games/mc/versions/1.20.1"), on.ModsFolder);
    }

    [Fact]
    public void Resolve_ModdableOnly_IsolatesOnlyModdableVersions()
    {
        var moddable = ModsFolderResolver.Resolve(
            "/games/mc",
            Version("1.20.1-fabric", LoaderKind.Fabric),
            VersionIsolationDefault.ModdableOnly);
        Assert.True(moddable.Isolated);

        var vanilla = ModsFolderResolver.Resolve(
            "/games/mc",
            Version("1.20.1"),
            VersionIsolationDefault.ModdableOnly);
        Assert.False(vanilla.Isolated);
        Assert.Equal(ModsFolder("/games/mc"), vanilla.ModsFolder);
    }

    [Fact]
    public void Resolve_NoSelectedVersion_FallsBackToSharedFolder()
    {
        var resolved = ModsFolderResolver.Resolve(
            "/games/mc",
            null,
            VersionIsolationDefault.All);

        Assert.False(resolved.Isolated);
        Assert.Equal(ModsFolder("/games/mc"), resolved.ModsFolder);
    }

    [Fact]
    public void Resolve_EmptyBaseFolder_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ModsFolderResolver.Resolve("  ", Version("1.20.1"), VersionIsolationDefault.All));
    }
}
