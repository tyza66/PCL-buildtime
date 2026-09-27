namespace PCL.Avalonia.Services.Minecraft;

public interface IVersionManagerService
{
    VersionSettings LoadSettings(string minecraftFolder, string versionId);

    void SetFavorite(string minecraftFolder, string versionId, bool isFavorite);

    void SetHidden(string minecraftFolder, string versionId, bool isHidden);

    void SetDisplayType(string minecraftFolder, string versionId, InstanceDisplayType displayType);

    void SetDescription(string minecraftFolder, string versionId, string description);

    /// <summary>设置版本隔离开关；null 表示恢复为跟随全局默认的自动判断。</summary>
    void SetInstanceIsolation(string minecraftFolder, string versionId, bool? independent);

    void SetInstanceLaunchSettings(
        string minecraftFolder,
        string versionId,
        int? maxMemoryMb,
        string? javaPath,
        string? jvmArguments,
        string? gameArguments);

    string Rename(string minecraftFolder, string versionId, string newName);

    void Delete(string minecraftFolder, string versionId);
}
