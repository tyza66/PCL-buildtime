using System.Text.Json;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class VersionMetadataBuilderTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _tempRoot;

    public VersionMetadataBuilderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaMetadata", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private MinecraftVersion Apply(string rawJson, string? folderName = null)
    {
        var json = JsonSerializer.Deserialize<MinecraftVersionJson>(rawJson, JsonOptions)!;
        var id = json.Id ?? "1.20.1";
        var folder = Path.Combine(_tempRoot, "versions", folderName ?? id);
        return VersionMetadataBuilder.WithMetadata(
            new MinecraftVersion
            {
                Id = id,
                Folder = folder,
                JsonPath = Path.Combine(folder, id + ".json"),
                Type = string.IsNullOrWhiteSpace(json.Type) ? "release" : json.Type,
                ReleaseTime = json.ReleaseTime ?? DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                InheritsFrom = json.InheritsFrom,
                MainClass = json.MainClass,
                Assets = json.Assets,
                AssetIndexId = json.AssetIndex?.Id,
                Jar = json.Jar,
            },
            json,
            rawJson);
    }

    [Fact]
    public void MissingMainClass_ProducesError()
    {
        var raw = """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2024-01-01T00:00:00Z"
        }
        """;

        var version = Apply(raw);

        Assert.Equal(InstanceState.Error, version.State);
    }

    [Fact]
    public void MissingDependency_ProducesError_UntilParentIsInstalled()
    {
        var raw = """
        {
          "id": "fabric-loader-1.20.1",
          "type": "release",
          "releaseTime": "2024-01-01T00:00:00Z",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "inheritsFrom": "1.20.1"
        }
        """;

        var missing = Apply(raw);
        Assert.Equal(InstanceState.Error, missing.State);

        var parentFolder = Path.Combine(_tempRoot, "versions", "1.20.1");
        Directory.CreateDirectory(parentFolder);
        File.WriteAllText(Path.Combine(parentFolder, "1.20.1.json"), "{}");

        var installed = Apply(raw);
        Assert.Equal(InstanceState.Original, installed.State);
        Assert.Equal("1.20.1", installed.VanillaName);
    }

    [Fact]
    public void OldReleaseTime_ResolvesOld()
    {
        var raw = """
        {
          "id": "b1.7.3",
          "type": "release",
          "releaseTime": "2011-06-30T00:00:00Z",
          "mainClass": "net.minecraft.launcher.Launcher"
        }
        """;

        var version = Apply(raw);

        Assert.Equal("Old", version.VanillaName);
        Assert.Equal(InstanceState.Old, version.State);
        Assert.Equal(209, version.Drop);
        Assert.True(version.Reliable);
    }

    [Fact]
    public void PendingType_ResolvesAsSnapshot()
    {
        var raw = """
        {
          "id": "pending-variant",
          "type": "pending",
          "releaseTime": "2024-09-01T00:00:00Z",
          "mainClass": "net.minecraft.client.main.Main"
        }
        """;

        var version = Apply(raw);

        Assert.Equal("pending", version.VanillaName);
        Assert.Equal(InstanceState.Snapshot, version.State);
        Assert.Equal(209, version.Drop);
    }

    [Fact]
    public void ClientVersion_TakesPriorityOverJsonId()
    {
        var raw = """
        {
          "id": "custom-1.20.4-java8",
          "type": "release",
          "releaseTime": "2024-01-01T00:00:00Z",
          "mainClass": "net.minecraft.client.main.Main",
          "clientVersion": "1.20.4"
        }
        """;

        var version = Apply(raw);

        Assert.Equal("1.20.4", version.VanillaName);
        Assert.Equal(200, version.Drop);
        Assert.Equal(InstanceState.Original, version.State);
    }

    [Fact]
    public void ForgeLibrary_ResolvesForgeStateAndLoaderVersion()
    {
        var raw = """
        {
          "id": "forge-1.20.1-47.2.0",
          "type": "release",
          "releaseTime": "2023-06-12T00:00:00Z",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": [
            { "name": "net.minecraftforge:forge:1.20.1-47.2.0" }
          ]
        }
        """;

        var version = Apply(raw);

        Assert.Equal(InstanceState.Forge, version.State);
        Assert.Equal(LoaderKind.Forge, version.Loader);
        Assert.Equal("47.2.0", version.LoaderVersion);
        Assert.Equal(200, version.Drop);
    }

    [Fact]
    public void OptiFineLibrary_ResolvesOptiFineStateAndLoaderVersion()
    {
        var raw = """
        {
          "id": "1.20.1",
          "type": "release",
          "releaseTime": "2023-06-12T00:00:00Z",
          "mainClass": "net.minecraft.client.main.Main",
          "libraries": [
            { "name": "optifine:OptiFine:HD_U_G5" }
          ]
        }
        """;

        var version = Apply(raw);

        Assert.Equal(InstanceState.OptiFine, version.State);
        Assert.Equal(LoaderKind.OptiFine, version.Loader);
        Assert.Equal("G5", version.LoaderVersion);
        Assert.Equal("1.20.1", version.VanillaName);
        Assert.Equal(200, version.Drop);
    }

    [Fact]
    public void FabricLoaderLibrary_ResolvesFabricStateAndLoaderVersion()
    {
        var raw = """
        {
          "id": "fabric-loader-1.21.1",
          "type": "release",
          "releaseTime": "2024-08-01T00:00:00Z",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "inheritsFrom": "1.21.1",
          "libraries": [
            { "name": "net.fabricmc:fabric-loader:0.16.9+build.10" }
          ]
        }
        """;

        var parentFolder = Path.Combine(_tempRoot, "versions", "1.21.1");
        Directory.CreateDirectory(parentFolder);
        File.WriteAllText(Path.Combine(parentFolder, "1.21.1.json"), "{}");

        var version = Apply(raw);

        Assert.Equal(InstanceState.Fabric, version.State);
        Assert.Equal(LoaderKind.Fabric, version.Loader);
        Assert.Equal("0.16.9.10", version.LoaderVersion);
        Assert.Equal("1.21.1", version.VanillaName);
        Assert.Equal(210, version.Drop);
    }

    [Fact]
    public void SnapshotName_ResolvesSnapshot_AndFallbackDrop()
    {
        var raw = """
        {
          "id": "24w10a",
          "type": "snapshot",
          "releaseTime": "2024-03-06T00:00:00Z",
          "mainClass": "net.minecraft.client.main.Main"
        }
        """;

        var version = Apply(raw);

        Assert.Equal("24w10a", version.VanillaName);
        Assert.Equal(InstanceState.Snapshot, version.State);
        Assert.Equal(209, version.Drop);
    }

    [Fact]
    public void FoolVersion_ResolvesFool_WithBaseReleaseDrop()
    {
        var raw = """
        {
          "id": "15w14a",
          "type": "snapshot",
          "releaseTime": "2015-04-01T12:00:00Z",
          "mainClass": "net.minecraft.client.main.Main"
        }
        """;

        var version = Apply(raw);

        Assert.Equal(InstanceState.Fool, version.State);
        Assert.Equal(80, version.Drop);
        Assert.Equal("2015 | 作为一款全年龄向的游戏，我们需要和平，需要爱与拥抱。", VersionMetadataBuilder.GetMcFoolName("15w14a"));
    }

    [Fact]
    public void IsFormatFit_MatchesOriginalFormat()
    {
        Assert.True(VersionMetadataBuilder.IsFormatFit("1.20.1"));
        Assert.True(VersionMetadataBuilder.IsFormatFit("26.1"));
        Assert.True(VersionMetadataBuilder.IsFormatFit("1.0"));
        Assert.False(VersionMetadataBuilder.IsFormatFit("24w10a"));
        Assert.False(VersionMetadataBuilder.IsFormatFit("2.0"));
        Assert.False(VersionMetadataBuilder.IsFormatFit(null));
    }
}
