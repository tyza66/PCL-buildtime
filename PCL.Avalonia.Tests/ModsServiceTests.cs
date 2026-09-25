using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class ModsServiceTests : IDisposable
{
    private readonly string _minecraftFolder;

    public ModsServiceTests()
    {
        _minecraftFolder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaMods", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_minecraftFolder))
        {
            Directory.Delete(_minecraftFolder, recursive: true);
        }
    }

    private string ModsFolder => Path.Combine(_minecraftFolder, "mods");

    private void WriteMod(string fileName, int size = 12)
    {
        Directory.CreateDirectory(ModsFolder);
        File.WriteAllBytes(Path.Combine(ModsFolder, fileName), new byte[size]);
    }

    [Fact]
    public void Scan_MissingFolder_ReturnsEmpty()
    {
        Assert.Empty(new ModsService().Scan(_minecraftFolder));
    }

    [Fact]
    public void Scan_ListsJarAndDisabledMods_IgnoresOtherFiles()
    {
        WriteMod("OptiFine.jar");
        WriteMod("fabric-api.jar.disabled");
        WriteMod("README.txt");

        var mods = new ModsService().Scan(_minecraftFolder);

        Assert.Equal(["fabric-api", "OptiFine"], mods.Select(mod => mod.DisplayName));
        Assert.False(mods[0].IsEnabled);
        Assert.True(mods[1].IsEnabled);
        Assert.Equal(12, mods[0].SizeBytes);
    }

    [Fact]
    public void SetEnabled_TogglesByRenaming_AndCanRestore()
    {
        WriteMod("example.jar");
        var service = new ModsService();
        var enabled = service.Scan(_minecraftFolder).Single();

        var disabled = service.SetEnabled(enabled, enabled: false);

        Assert.False(disabled.IsEnabled);
        Assert.Equal("example.jar.disabled", disabled.FileName);
        Assert.True(File.Exists(Path.Combine(ModsFolder, "example.jar.disabled")));
        Assert.False(File.Exists(Path.Combine(ModsFolder, "example.jar")));

        var restored = service.SetEnabled(disabled, enabled: true);

        Assert.True(restored.IsEnabled);
        Assert.Equal("example.jar", restored.FileName);
        Assert.True(File.Exists(Path.Combine(ModsFolder, "example.jar")));
    }

    [Fact]
    public void Delete_RemovesModFile()
    {
        WriteMod("example.jar");
        var mod = new ModsService().Scan(_minecraftFolder).Single();

        new ModsService().Delete(mod);

        Assert.False(File.Exists(mod.FilePath));
    }
}
