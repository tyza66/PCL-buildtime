namespace PCL.Avalonia.Services.Mods;

public interface IInstancePackExporter
{
    string Export(
        string minecraftFolder,
        string versionId,
        string displayName,
        string outputPath);
}
