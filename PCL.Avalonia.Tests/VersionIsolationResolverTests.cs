using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class VersionIsolationResolverTests : IDisposable
{
    private readonly string _root;

    public VersionIsolationResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcl-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static MinecraftVersion MakeVersion(InstanceState state, LoaderKind loader = LoaderKind.None) =>
        new()
        {
            Id = "1.20.1",
            Folder = Path.Combine(Path.GetTempPath(), "no-such-folder"),
            JsonPath = "1.20.1.json",
            Type = "release",
            State = state,
            Loader = loader,
        };

    private string MakeVersionFolder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Theory]
    [InlineData(VersionIsolationDefault.Off, InstanceState.Original, LoaderKind.None, false)]
    [InlineData(VersionIsolationDefault.Off, InstanceState.Snapshot, LoaderKind.None, false)]
    [InlineData(VersionIsolationDefault.Off, InstanceState.Original, LoaderKind.Fabric, false)]
    [InlineData(VersionIsolationDefault.ModdableOnly, InstanceState.Original, LoaderKind.None, false)]
    [InlineData(VersionIsolationDefault.ModdableOnly, InstanceState.Original, LoaderKind.Fabric, true)]
    [InlineData(VersionIsolationDefault.ModdableOnly, InstanceState.Original, LoaderKind.NeoForge, true)]
    [InlineData(VersionIsolationDefault.ModdableOnly, InstanceState.Snapshot, LoaderKind.None, false)]
    [InlineData(VersionIsolationDefault.SnapshotOnly, InstanceState.Original, LoaderKind.None, false)]
    [InlineData(VersionIsolationDefault.SnapshotOnly, InstanceState.Snapshot, LoaderKind.None, true)]
    [InlineData(VersionIsolationDefault.SnapshotOnly, InstanceState.Old, LoaderKind.None, true)]
    [InlineData(VersionIsolationDefault.SnapshotOnly, InstanceState.Fool, LoaderKind.None, true)]
    [InlineData(VersionIsolationDefault.SnapshotOnly, InstanceState.Original, LoaderKind.Fabric, false)]
    [InlineData(VersionIsolationDefault.SnapshotAndModdable, InstanceState.Original, LoaderKind.None, false)]
    [InlineData(VersionIsolationDefault.SnapshotAndModdable, InstanceState.Original, LoaderKind.Fabric, true)]
    [InlineData(VersionIsolationDefault.SnapshotAndModdable, InstanceState.Snapshot, LoaderKind.None, true)]
    [InlineData(VersionIsolationDefault.All, InstanceState.Original, LoaderKind.None, true)]
    [InlineData(VersionIsolationDefault.All, InstanceState.Fool, LoaderKind.None, true)]
    public void IsIsolated_FollowsGlobalDefault_WhenNoManualOverride(
        VersionIsolationDefault globalDefault,
        InstanceState state,
        LoaderKind loader,
        bool expected)
    {
        var version = MakeVersion(state, loader);

        Assert.Equal(
            expected,
            VersionIsolationResolver.IsIsolated(version, globalDefault));
    }

    [Fact]
    public void IsIsolated_IsolatesAll_ForUnknownGlobalValue()
    {
        var version = MakeVersion(InstanceState.Original);

        Assert.True(VersionIsolationResolver.IsIsolated(version, (VersionIsolationDefault)99));
    }

    [Fact]
    public void IsIsolated_PrefersManualOverride_OverGlobalDefault()
    {
        var version = MakeVersion(InstanceState.Original, LoaderKind.Fabric);

        Assert.True(VersionIsolationResolver.IsIsolated(
            version,
            VersionIsolationDefault.Off,
            new VersionSettings { Independent = true }));
        Assert.False(VersionIsolationResolver.IsIsolated(
            version,
            VersionIsolationDefault.All,
            new VersionSettings { Independent = false }));
    }

    [Fact]
    public void IsIsolated_AutoEnables_WhenModFolderHasFiles()
    {
        var folder = MakeVersionFolder("with-mods");
        Directory.CreateDirectory(Path.Combine(folder, "mods"));
        File.WriteAllText(Path.Combine(folder, "mods", "a.jar"), "");
        var version = MakeVersion(InstanceState.Original) with { Folder = folder };

        Assert.True(VersionIsolationResolver.IsIsolated(version, VersionIsolationDefault.Off));
    }

    [Fact]
    public void IsIsolated_AutoEnables_WhenSavesFolderHasWorlds()
    {
        var folder = MakeVersionFolder("with-saves");
        Directory.CreateDirectory(Path.Combine(folder, "saves", "MyWorld"));
        var version = MakeVersion(InstanceState.Original) with { Folder = folder };

        Assert.True(VersionIsolationResolver.IsIsolated(version, VersionIsolationDefault.Off));
    }

    [Fact]
    public void IsIsolated_IgnoresEmptyModsAndSavesFolders()
    {
        var folder = MakeVersionFolder("empty-folders");
        Directory.CreateDirectory(Path.Combine(folder, "mods"));
        Directory.CreateDirectory(Path.Combine(folder, "saves"));
        var version = MakeVersion(InstanceState.Original) with { Folder = folder };

        Assert.False(VersionIsolationResolver.IsIsolated(version, VersionIsolationDefault.Off));
    }

    [Fact]
    public void IsIsolated_ManualOverrideBeatsDetectedMods()
    {
        var folder = MakeVersionFolder("manual-off");
        Directory.CreateDirectory(Path.Combine(folder, "mods"));
        File.WriteAllText(Path.Combine(folder, "mods", "a.jar"), "");
        var version = MakeVersion(InstanceState.Original) with { Folder = folder };

        Assert.False(VersionIsolationResolver.IsIsolated(
            version,
            VersionIsolationDefault.All,
            new VersionSettings { Independent = false }));
    }

    [Fact]
    public void ResolveGameDirectory_PointsAtVersionFolder_WhenIsolated()
    {
        var folder = MakeVersionFolder("isolated");
        var version = MakeVersion(InstanceState.Original) with { Folder = folder };

        var resolved = VersionIsolationResolver.ResolveGameDirectory(_root, version, VersionIsolationDefault.All);

        Assert.Equal(folder, resolved);
    }

    [Fact]
    public void ResolveGameDirectory_PointsAtBaseFolder_WhenNotIsolated()
    {
        var folder = MakeVersionFolder("shared");
        var version = MakeVersion(InstanceState.Original) with { Folder = folder };

        var resolved = VersionIsolationResolver.ResolveGameDirectory(_root, version, VersionIsolationDefault.Off);

        Assert.Equal(_root, resolved);
    }

    [Fact]
    public void HasModsOrSaves_ReturnsFalse_ForMissingFolder()
    {
        Assert.False(VersionIsolationResolver.HasModsOrSaves(null));
        Assert.False(VersionIsolationResolver.HasModsOrSaves(""));
        Assert.False(VersionIsolationResolver.HasModsOrSaves(Path.Combine(_root, "missing")));
    }
}
