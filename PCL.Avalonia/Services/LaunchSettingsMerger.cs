using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Services;

public static class LaunchSettingsMerger
{
    public static AppSettings Merge(AppSettings settings, VersionSettings? versionSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (versionSettings is null)
        {
            return settings;
        }

        return settings with
        {
            MaxMemoryMb = versionSettings.MaxMemoryMb is > 0
                ? versionSettings.MaxMemoryMb.Value
                : settings.MaxMemoryMb,
            JavaPath = string.IsNullOrWhiteSpace(versionSettings.JavaPath)
                ? settings.JavaPath
                : versionSettings.JavaPath.Trim(),
            JvmArguments = string.IsNullOrWhiteSpace(versionSettings.JvmArguments)
                ? settings.JvmArguments
                : versionSettings.JvmArguments.Trim(),
            GameArguments = string.IsNullOrWhiteSpace(versionSettings.GameArguments)
                ? settings.GameArguments
                : versionSettings.GameArguments.Trim(),
        };
    }
}
