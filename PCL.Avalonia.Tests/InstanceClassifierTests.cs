using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class InstanceClassifierTests
{
    private static readonly DateTimeOffset DefaultTime = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    private static VersionInstance Instance(
        string id,
        InstanceState state = InstanceState.Original,
        DateTimeOffset? releaseTime = null,
        int drop = 0,
        LoaderKind loader = LoaderKind.None,
        string? loaderVersion = null,
        string? vanillaName = null,
        bool favorite = false,
        bool hidden = false,
        InstanceDisplayType displayType = InstanceDisplayType.Auto)
    {
        var version = new MinecraftVersion
        {
            Id = id,
            Folder = $"/mc/versions/{id}",
            JsonPath = $"/mc/versions/{id}/{id}.json",
            State = state,
            ReleaseTime = releaseTime ?? DefaultTime,
            Drop = drop,
            Loader = loader,
            LoaderVersion = loaderVersion,
            VanillaName = vanillaName ?? id,
        };
        var settings = new VersionSettings
        {
            IsFavorite = favorite,
            IsHidden = hidden,
            DisplayType = displayType,
        };
        return new VersionInstance(version, settings);
    }

    private static IReadOnlyDictionary<InstanceGroup, IReadOnlyList<VersionInstance>> Group(
        bool showHidden,
        params VersionInstance[] instances)
        => new InstanceClassifier().Group(instances, showHidden);

    [Fact]
    public void Favorite_InstancesMoveToStar_EvenWhenStateIsError()
    {
        var favoriteError = Instance(
            "err",
            state: InstanceState.Error,
            releaseTime: DateTimeOffset.Parse("2023-12-01T00:00:00Z"),
            favorite: true);
        var favoriteRelease = Instance(
            "good",
            releaseTime: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            favorite: true);
        var normal = Instance(
            "normal",
            releaseTime: DateTimeOffset.Parse("2024-02-01T00:00:00Z"),
            drop: 200);

        var result = Group(showHidden: false, favoriteError, favoriteRelease, normal);

        Assert.Equal(["good", "err"], result[InstanceGroup.Star].Select(v => v.Version.Id));
        Assert.DoesNotContain(favoriteError, result[InstanceGroup.Error]);
        Assert.Equal(["normal"], result[InstanceGroup.OriginalLike].Select(v => v.Version.Id));
    }

    [Fact]
    public void Error_Fool_AndApiStates_AreClassifiedIntoTheirCards()
    {
        var result = Group(
            showHidden: false,
            Instance("broken", state: InstanceState.Error),
            Instance("fool", state: InstanceState.Fool),
            Instance("forge", state: InstanceState.Forge, loader: LoaderKind.Forge),
            Instance("neoforge", state: InstanceState.NeoForge, loader: LoaderKind.NeoForge),
            Instance("fabric", state: InstanceState.Fabric, loader: LoaderKind.Fabric),
            Instance("liteloader", state: InstanceState.LiteLoader, loader: LoaderKind.LiteLoader));

        Assert.Equal(["broken"], result[InstanceGroup.Error].Select(v => v.Version.Id));
        Assert.Equal(["fool"], result[InstanceGroup.Fool].Select(v => v.Version.Id));
        Assert.Equal(
            ["fabric", "neoforge", "forge", "liteloader"],
            result[InstanceGroup.Api].Select(v => v.Version.Id));
        Assert.Empty(result[InstanceGroup.OriginalLike]);
        Assert.Empty(result[InstanceGroup.Rubbish]);
    }

    [Fact]
    public void LatestSnapshot_StaysUseful_OldAndOlderSnapshots_GoToRubbish()
    {
        var release = Instance(
            "1.20.1",
            state: InstanceState.Original,
            releaseTime: DateTimeOffset.Parse("2023-06-12T00:00:00Z"),
            drop: 200);
        var old = Instance(
            "b1.7.3",
            state: InstanceState.Old,
            releaseTime: DateTimeOffset.Parse("2011-06-30T00:00:00Z"),
            drop: 209);
        var latestSnapshot = Instance(
            "24w10a",
            state: InstanceState.Snapshot,
            releaseTime: DateTimeOffset.Parse("2024-03-06T00:00:00Z"),
            drop: 209);
        var olderSnapshot = Instance(
            "23w45a",
            state: InstanceState.Snapshot,
            releaseTime: DateTimeOffset.Parse("2023-11-09T00:00:00Z"),
            drop: 209);

        var result = Group(showHidden: false, release, old, latestSnapshot, olderSnapshot);

        Assert.Equal(
            ["24w10a", "1.20.1"],
            result[InstanceGroup.OriginalLike].Select(v => v.Version.Id));
        Assert.Equal(
            ["23w45a", "b1.7.3"],
            result[InstanceGroup.Rubbish].Select(v => v.Version.Id));
    }

    [Fact]
    public void Drop_KeepsNewestOriginal_AndHighestOptiFineCode()
    {
        var oldOriginal = Instance(
            "1.20.1",
            releaseTime: DateTimeOffset.Parse("2023-06-12T00:00:00Z"),
            drop: 200);
        var newOriginal = Instance(
            "1.20.2",
            releaseTime: DateTimeOffset.Parse("2023-09-21T00:00:00Z"),
            drop: 200);
        var oldOptiFine = Instance(
            "1.21-OptiFine_G5",
            state: InstanceState.OptiFine,
            releaseTime: DateTimeOffset.Parse("2024-04-01T00:00:00Z"),
            drop: 210,
            loader: LoaderKind.OptiFine,
            loaderVersion: "G5");
        var newOptiFine = Instance(
            "1.21-OptiFine_H5",
            state: InstanceState.OptiFine,
            releaseTime: DateTimeOffset.Parse("2024-06-01T00:00:00Z"),
            drop: 210,
            loader: LoaderKind.OptiFine,
            loaderVersion: "H5");
        var baseOriginal = Instance(
            "1.21",
            releaseTime: DateTimeOffset.Parse("2024-06-13T00:00:00Z"),
            drop: 210);

        var result = Group(
            showHidden: false,
            oldOriginal,
            newOriginal,
            oldOptiFine,
            newOptiFine,
            baseOriginal);

        Assert.Equal(
            ["1.21-OptiFine_H5", "1.20.2"],
            result[InstanceGroup.OriginalLike].Select(v => v.Version.Id));
        Assert.Equal(
            ["1.21", "1.21-OptiFine_G5", "1.20.1"],
            result[InstanceGroup.Rubbish].Select(v => v.Version.Id));
    }

    [Fact]
    public void CustomDisplayType_MovesVersions_ButStar_IsNeverMoved()
    {
        var customApi = Instance("api-custom", displayType: InstanceDisplayType.Api);
        var customRubbish = Instance("rubbish-custom", displayType: InstanceDisplayType.Rubbish);
        var customFool = Instance("fool-custom", state: InstanceState.Fool, displayType: InstanceDisplayType.Original);
        var star = Instance("star", favorite: true, displayType: InstanceDisplayType.Rubbish);
        var hiddenSwap = Instance("hidden-display", displayType: InstanceDisplayType.Hidden);
        var hiddenSetting = Instance("hidden-setting", hidden: true);
        var hiddenFavorite = Instance("hidden-favorite", favorite: true, hidden: true);

        var hidden = Group(
            showHidden: false,
            customApi,
            customRubbish,
            customFool,
            star,
            hiddenSwap,
            hiddenSetting,
            hiddenFavorite);

        Assert.Equal(["star"], hidden[InstanceGroup.Star].Select(v => v.Version.Id));
        Assert.Equal(["api-custom"], hidden[InstanceGroup.Api].Select(v => v.Version.Id));
        Assert.Equal(["fool-custom"], hidden[InstanceGroup.OriginalLike].Select(v => v.Version.Id));
        Assert.Equal(["rubbish-custom"], hidden[InstanceGroup.Rubbish].Select(v => v.Version.Id));
        Assert.Empty(hidden[InstanceGroup.Hidden]);

        var shown = Group(
            showHidden: true,
            customApi,
            customRubbish,
            customFool,
            star,
            hiddenSwap,
            hiddenSetting,
            hiddenFavorite);

        Assert.Equal(["star"], shown[InstanceGroup.Star].Select(v => v.Version.Id));
        Assert.Equal(
            ["hidden-display", "hidden-favorite", "hidden-setting"],
            shown[InstanceGroup.Hidden].Select(v => v.Version.Id).OrderBy(id => id));
        Assert.Empty(shown[InstanceGroup.Api].Where(v => v.IsHidden));
        Assert.Empty(shown[InstanceGroup.OriginalLike].Where(v => v.IsHidden));
        Assert.Empty(shown[InstanceGroup.Rubbish].Where(v => v.IsHidden));
    }

    [Fact]
    public void UnknownName_ProducesInvalidDrop_AndStaysOutOfUseful()
    {
        var unknown = Instance("weird", drop: 209 * 10 + 9);
        var result = Group(showHidden: false, unknown);

        Assert.Empty(result[InstanceGroup.OriginalLike]);
        Assert.Equal(["weird"], result[InstanceGroup.Rubbish].Select(v => v.Version.Id));
    }
}
