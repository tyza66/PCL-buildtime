namespace PCL.Avalonia.Services.Minecraft;

public interface IVersionManagerService
{
    VersionSettings LoadSettings(string minecraftFolder, string versionId);

    void SetFavorite(string minecraftFolder, string versionId, bool isFavorite);

    void SetHidden(string minecraftFolder, string versionId, bool isHidden);

    void SetDescription(string minecraftFolder, string versionId, string description);

    string Rename(string minecraftFolder, string versionId, string newName);

    void Delete(string minecraftFolder, string versionId);
}
