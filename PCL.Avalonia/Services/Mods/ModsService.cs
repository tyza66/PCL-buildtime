namespace PCL.Avalonia.Services.Mods;

public sealed class ModsService : IModsService
{
    private const string DisabledSuffix = ".jar.disabled";
    private const string JarSuffix = ".jar";

    public IReadOnlyList<ModInfo> Scan(string minecraftFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftFolder);
        var modsFolder = Path.Combine(minecraftFolder, "mods");
        if (!Directory.Exists(modsFolder))
        {
            return [];
        }

        var result = new List<ModInfo>();
        foreach (var file in Directory.EnumerateFiles(modsFolder))
        {
            var fileName = Path.GetFileName(file);
            var isDisabled = fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            var isJar = !isDisabled && fileName.EndsWith(JarSuffix, StringComparison.OrdinalIgnoreCase);
            if (!isDisabled && !isJar)
            {
                continue;
            }

            var info = new FileInfo(file);
            result.Add(new ModInfo
            {
                FileName = fileName,
                DisplayName = isDisabled ? fileName[..^DisabledSuffix.Length] : fileName[..^JarSuffix.Length],
                FilePath = file,
                IsEnabled = !isDisabled,
                SizeBytes = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc,
            });
        }

        return result
            .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ModInfo SetEnabled(ModInfo mod, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (mod.IsEnabled == enabled)
        {
            return mod;
        }

        var target = enabled
            ? mod.FilePath[..^DisabledSuffix.Length] + JarSuffix
            : mod.FilePath + ".disabled";
        File.Move(mod.FilePath, target);

        return new ModInfo
        {
            FileName = Path.GetFileName(target),
            DisplayName = mod.DisplayName,
            FilePath = target,
            IsEnabled = enabled,
            SizeBytes = mod.SizeBytes,
            LastModifiedUtc = DateTimeOffset.UtcNow,
        };
    }

    public void Delete(ModInfo mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        File.Delete(mod.FilePath);
    }
}
