namespace PCL.Avalonia.Services.Minecraft;

public interface IVersionCatalogService
{
    IReadOnlyList<MinecraftVersion> Scan(string minecraftFolder);

    MinecraftVersionJson? LoadJson(string minecraftFolder, string id);
}
