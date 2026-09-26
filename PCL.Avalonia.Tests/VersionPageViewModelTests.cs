using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Game;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.Services.Platform;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class VersionPageViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new AppSettings
        {
            MinecraftFolder = "/games/mc",
            JavaPath = "/usr/bin/java",
        };

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakeCatalog : IVersionCatalogService
    {
        public IReadOnlyList<MinecraftVersion> Installed { get; set; } = [];

        public List<string> ScannedFolders { get; } = [];

        public MinecraftVersionJson? Json { get; set; }

        public IReadOnlyList<MinecraftVersion> Scan(string minecraftFolder)
        {
            ScannedFolders.Add(minecraftFolder);
            return Installed;
        }

        public MinecraftVersionJson? LoadJson(string minecraftFolder, string id) => Json;
    }

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private sealed class FakeVersionManager : IVersionManagerService
    {
        public Dictionary<string, VersionSettings> SettingsByVersion { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Deleted { get; } = [];

        public List<(string OldId, string NewId)> Renamed { get; } = [];

        public VersionSettings LoadSettings(string minecraftFolder, string versionId)
            => SettingsByVersion.TryGetValue(versionId, out var settings)
                ? settings
                : new VersionSettings();

        public void SetFavorite(string minecraftFolder, string versionId, bool isFavorite)
            => SettingsByVersion[versionId] = LoadSettings(minecraftFolder, versionId) with { IsFavorite = isFavorite };

        public void SetHidden(string minecraftFolder, string versionId, bool isHidden)
            => SettingsByVersion[versionId] = LoadSettings(minecraftFolder, versionId) with { IsHidden = isHidden };

        public void SetDisplayType(string minecraftFolder, string versionId, InstanceDisplayType displayType)
            => SettingsByVersion[versionId] = LoadSettings(minecraftFolder, versionId) with { DisplayType = displayType };

        public void SetInstanceLaunchSettings(
            string minecraftFolder,
            string versionId,
            int? maxMemoryMb,
            string? javaPath,
            string? jvmArguments,
            string? gameArguments)
            => SettingsByVersion[versionId] = LoadSettings(minecraftFolder, versionId) with
            {
                MaxMemoryMb = maxMemoryMb,
                JavaPath = javaPath,
                JvmArguments = jvmArguments,
                GameArguments = gameArguments,
            };

        public void SetDescription(string minecraftFolder, string versionId, string description)
            => SettingsByVersion[versionId] = LoadSettings(minecraftFolder, versionId) with { Description = description };

        public string Rename(string minecraftFolder, string versionId, string newName)
        {
            Renamed.Add((versionId, newName));
            if (SettingsByVersion.TryGetValue(versionId, out var settings))
            {
                SettingsByVersion[newName] = settings;
            }

            return newName;
        }

        public void Delete(string minecraftFolder, string versionId)
        {
            Deleted.Add(versionId);
            SettingsByVersion.Remove(versionId);
        }
    }

    private sealed class FakeFolderOpener : IFolderOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string path) => Opened.Add(path);
    }

    private sealed class FakeJavaService : IJavaService
    {
        public string? ResolveJavaExecutable(AppSettings settings) => settings.JavaPath;

        public string? ResolveJavaExecutable(AppSettings settings, int? requiredMajorVersion)
            => ResolveJavaExecutable(settings);
    }

    private sealed class FakeJavaListService : IJavaListService
    {
        private readonly JavaInfo[] _items;

        public FakeJavaListService(params JavaInfo[] items) => _items = items;

        public IReadOnlyList<JavaInfo> Scan() => _items;

        public JavaInfo? GetJava(string path) => _items.FirstOrDefault(item => item.Path == path);

        public void Refresh()
        {
        }
    }

    private sealed class FakeGameLauncher : IGameLauncher
    {
        public LaunchPlan? LastPlan { get; private set; }

        public LaunchPlan BuildLaunchPlan(
            MinecraftVersion version,
            AppSettings settings,
            string javaExecutable,
            Account? account = null,
            VersionSettings? versionSettings = null)
        {
            LastPlan = new LaunchPlan
            {
                JavaExecutable = javaExecutable,
                WorkingDirectory = settings.MinecraftFolder,
                NativesDirectory = Path.Combine(version.Folder, version.Id + "-natives"),
                ClassPath = "a.jar",
                MainClass = "net.minecraft.client.main.Main",
                Arguments = ["-Xmx2G"],
                Version = version,
            };
            return LastPlan;
        }

        public IGameLaunch Launch(LaunchPlan plan, IProgress<string>? output = null)
            => throw new NotSupportedException();
    }

    private sealed class FakeScriptExporter : ILaunchScriptExporter
    {
        public List<(LaunchPlan Plan, string FilePath)> Exported { get; } = [];

        public string Export(LaunchPlan plan, string filePath)
        {
            Exported.Add((plan, filePath));
            return filePath;
        }
    }

    private sealed class FakeModsService : IModsService
    {
        public IReadOnlyList<ModInfo> Scan(string minecraftFolder) => [];

        public ModInfo SetEnabled(ModInfo mod, bool enabled) => mod;

        public void Delete(ModInfo mod)
        {
        }
    }

    private static MinecraftVersion Version(string id)
        => new()
        {
            Id = id,
            Folder = $"/games/mc/versions/{id}",
            JsonPath = $"/games/mc/versions/{id}/{id}.json",
        };

    private static (FakeCatalog Catalog, SessionState Session, VersionPageViewModel ViewModel) CreateViewModel(
        FakeCatalog catalog,
        SessionState session,
        FakeVersionManager? manager = null,
        FakeFolderOpener? folderOpener = null,
        FakeGameLauncher? launcher = null,
        FakeScriptExporter? exporter = null,
        FakeJavaListService? javaList = null,
        FakeSettingsService? settings = null)
    {
        var viewModel = new VersionPageViewModel(
            settings ?? new FakeSettingsService(),
            catalog,
            new InstanceClassifier(),
            session,
            new FakePlatformService(),
            manager ?? new FakeVersionManager(),
            folderOpener ?? new FakeFolderOpener(),
            new FakeJavaService(),
            javaList ?? new FakeJavaListService(),
            launcher ?? new FakeGameLauncher(),
            exporter ?? new FakeScriptExporter(),
            new InstancePackExporter(),
            new FakeModsService());
        return (catalog, session, viewModel);
    }

    [Fact]
    public void Refresh_SelectsFirstVersion()
    {
        var catalog = new FakeCatalog
        {
            Installed = [Version("1.20.1"), Version("1.19.4")],
        };
        var session = new SessionState();

        var (_, _, viewModel) = CreateViewModel(catalog, session);

        Assert.Equal("1.20.1", viewModel.SelectedVersion?.Id);
        Assert.Equal("1.20.1", session.SelectedVersion?.Id);
        Assert.Contains("已找到 2", viewModel.StatusMessage);
        Assert.Single(catalog.ScannedFolders);
    }

    [Fact]
    public void InstalledEvent_RefreshesAndSelectsInstalledVersion()
    {
        var session = new SessionState();
        var catalog = new FakeCatalog();
        var (_, _, viewModel) = CreateViewModel(catalog, session);

        catalog.Installed = [Version("1.20.1")];
        session.NotifyVersionInstalled("1.20.1");

        Assert.Equal("1.20.1", viewModel.SelectedVersion?.Id);
        Assert.Equal(2, catalog.ScannedFolders.Count);
        Assert.Contains("/games/mc", catalog.ScannedFolders);
    }

    [Fact]
    public void Refresh_LoadsInstanceSettingsAndFiltersHidden()
    {
        var manager = new FakeVersionManager
        {
            SettingsByVersion =
            {
                ["1.20.1"] = new VersionSettings { IsFavorite = true },
                ["1.19.4"] = new VersionSettings { IsHidden = true, Description = "旧版本" },
            },
        };
        var catalog = new FakeCatalog
        {
            Installed = [Version("1.20.1"), Version("1.19.4")],
        };

        var (_, _, viewModel) = CreateViewModel(catalog, new SessionState(), manager);

        Assert.Single(viewModel.Versions);
        Assert.True(viewModel.Versions[0].IsFavorite);
        Assert.Equal("1.20.1", viewModel.Versions[0].Id);

        viewModel.ShowHidden = true;

        Assert.Equal(2, viewModel.Versions.Count);
        Assert.True(viewModel.Versions[1].IsHidden);
        Assert.Equal("旧版本", viewModel.Versions[1].Description);
    }

    [Fact]
    public void ToggleFavorite_UpdatesManagerAndItem()
    {
        var manager = new FakeVersionManager();
        var (_, _, viewModel) = CreateViewModel(
            new FakeCatalog { Installed = [Version("1.20.1")] },
            new SessionState(),
            manager);

        viewModel.Versions[0].FavoriteCommand.Execute(null);

        Assert.True(manager.SettingsByVersion["1.20.1"].IsFavorite);
        Assert.True(viewModel.Versions[0].IsFavorite);
        Assert.Equal("取消收藏", viewModel.Versions[0].FavoriteButtonText);
    }

    [Fact]
    public void Delete_RemovesVersionAndSelection()
    {
        var manager = new FakeVersionManager();
        var (_, session, viewModel) = CreateViewModel(
            new FakeCatalog { Installed = [Version("1.20.1")] },
            new SessionState(),
            manager);

        viewModel.Versions[0].DeleteCommand.Execute(null);

        Assert.Equal(["1.20.1"], manager.Deleted);
        Assert.Empty(viewModel.Versions);
        Assert.Null(viewModel.SelectedItem);
        Assert.Null(session.SelectedVersion);
    }

    [Fact]
    public void OpenFolder_OpensVersionFolder()
    {
        var opener = new FakeFolderOpener();
        var (_, _, viewModel) = CreateViewModel(
            new FakeCatalog { Installed = [Version("1.20.1")] },
            new SessionState(),
            folderOpener: opener);

        viewModel.Versions[0].OpenFolderCommand.Execute(null);

        var expectedPath = Path.Combine("/games/mc", "versions", "1.20.1");
        Assert.Equal([expectedPath], opener.Opened);
    }

    [Fact]
    public void Rename_UpdatesManagerAndSelectsRenamedVersion()
    {
        var manager = new FakeVersionManager();
        var catalog = new FakeCatalog { Installed = [Version("1.20.1")] };
        var (_, session, viewModel) = CreateViewModel(
            catalog,
            new SessionState(),
            manager);

        catalog.Installed = [Version("1.20.2")];
        viewModel.NewName = "1.20.2";
        viewModel.RenameCommand.Execute(null);

        Assert.Equal([("1.20.1", "1.20.2")], manager.Renamed);
        Assert.Equal("1.20.2", viewModel.SelectedVersion?.Id);
        Assert.Equal("1.20.2", session.SelectedVersion?.Id);
        Assert.Contains("重命名成功", viewModel.StatusMessage);
    }

    [Fact]
    public void SaveDescription_UpdatesManagerAndItem()
    {
        var manager = new FakeVersionManager();
        var (_, _, viewModel) = CreateViewModel(
            new FakeCatalog { Installed = [Version("1.20.1")] },
            new SessionState(),
            manager);

        viewModel.DescriptionInput = "我的整合包";
        viewModel.SaveDescriptionCommand.Execute(null);

        Assert.Equal("我的整合包", manager.SettingsByVersion["1.20.1"].Description);
        Assert.Equal("我的整合包", viewModel.Versions[0].Description);
        Assert.Contains("描述已保存", viewModel.StatusMessage);
    }

    [Fact]
    public void ExportScript_BuildsPlanAndWritesScript()
    {
        var launcher = new FakeGameLauncher();
        var exporter = new FakeScriptExporter();
        var (_, session, viewModel) = CreateViewModel(
            new FakeCatalog { Installed = [Version("1.20.1")] },
            new SessionState(),
            launcher: launcher,
            exporter: exporter);

        viewModel.ExportScriptCommand.Execute(null);

        var exported = Assert.Single(exporter.Exported);
        Assert.Equal("1.20.1", exported.Plan.Version.Id);
        var expectedName = OperatingSystem.IsWindows() ? "launch.bat" : "launch.sh";
        Assert.EndsWith(expectedName, exported.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1.20.1", session.SelectedVersion?.Id);
        Assert.Contains("已导出启动脚本", viewModel.StatusMessage);
    }

    private static FakeCatalog JavaHintCatalog(int majorVersion)
        => new()
        {
            Installed = [Version("1.20.1")],
            Json = new MinecraftVersionJson { JavaVersion = new JavaVersionJson { MajorVersion = majorVersion } },
        };

    [Fact]
    public void JavaHint_SaysTheRequirementIsMet_WhenTheCurrentJavaIsNewEnough()
    {
        var (_, _, viewModel) = CreateViewModel(
            JavaHintCatalog(21),
            new SessionState(),
            javaList: new FakeJavaListService(new JavaInfo("/usr/bin/java", "21.0.2", "aarch64", 21, true)));

        Assert.Contains("该版本需要 Java 21", viewModel.JavaHintText);
        Assert.Contains("满足要求", viewModel.JavaHintText);
        Assert.False(viewModel.JavaHintIsWarning);
    }

    [Fact]
    public void JavaHint_Warns_WhenTheCurrentJavaIsOlderThanTheVersionNeeds()
    {
        var (_, _, viewModel) = CreateViewModel(
            JavaHintCatalog(21),
            new SessionState(),
            javaList: new FakeJavaListService(new JavaInfo("/usr/bin/java", "17.0.9", "x86_64", 17, true)));

        Assert.Contains("该版本需要 Java 21", viewModel.JavaHintText);
        Assert.Contains("当前会用到 Java 17", viewModel.JavaHintText);
        Assert.True(viewModel.JavaHintIsWarning);
    }

    [Fact]
    public void JavaHint_PrefersTheInstanceJavaPath_OverTheGlobalOne()
    {
        var manager = new FakeVersionManager
        {
            SettingsByVersion =
            {
                ["1.20.1"] = new VersionSettings { JavaPath = "/opt/jdk17/bin/java" },
            },
        };
        var javaList = new FakeJavaListService(
            new JavaInfo("/usr/bin/java", "21.0.2", "aarch64", 21, true),
            new JavaInfo("/opt/jdk17/bin/java", "17.0.9", "x86_64", 17, true));

        var (_, _, viewModel) = CreateViewModel(
            JavaHintCatalog(21),
            new SessionState(),
            manager,
            javaList: javaList);

        Assert.Contains("当前会用到 Java 17", viewModel.JavaHintText);
        Assert.True(viewModel.JavaHintIsWarning);
    }

    [Fact]
    public void JavaHint_Warns_WhenNoJavaIsDetectedAtAll()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings { MinecraftFolder = "/games/mc", JavaPath = "" },
        };

        var (_, _, viewModel) = CreateViewModel(
            JavaHintCatalog(21),
            new SessionState(),
            settings: settings);

        Assert.Contains("该版本需要 Java 21", viewModel.JavaHintText);
        Assert.Contains("但当前没有检测到 Java", viewModel.JavaHintText);
        Assert.True(viewModel.JavaHintIsWarning);
    }

    [Fact]
    public void JavaHint_SaysNotProvided_WhenTheVersionJsonHasNoJavaVersion()
    {
        var catalog = new FakeCatalog { Installed = [Version("1.20.1")], Json = null };

        var (_, _, viewModel) = CreateViewModel(
            catalog,
            new SessionState(),
            javaList: new FakeJavaListService(new JavaInfo("/usr/bin/java", "21.0.2", "aarch64", 21, true)));

        Assert.Contains("未提供 Java 要求", viewModel.JavaHintText);
        Assert.Contains("当前将使用 Java 21", viewModel.JavaHintText);
        Assert.False(viewModel.JavaHintIsWarning);
    }

    [Fact]
    public void JavaHint_Clears_WhenTheSelectionIsRemoved()
    {
        var (_, _, viewModel) = CreateViewModel(
            JavaHintCatalog(21),
            new SessionState(),
            javaList: new FakeJavaListService(new JavaInfo("/usr/bin/java", "21.0.2", "aarch64", 21, true)));
        Assert.NotEmpty(viewModel.JavaHintText);

        viewModel.SelectedItem = null;

        Assert.Empty(viewModel.JavaHintText);
    }
}
