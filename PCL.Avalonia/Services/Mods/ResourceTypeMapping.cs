namespace PCL.Avalonia.Services.Mods;

public static class ResourceTypeMapping
{
    public static string ToModrinthProjectType(this ResourceType type) => type switch
    {
        ResourceType.Mod => "mod",
        ResourceType.ModPack => "modpack",
        ResourceType.ResourcePack => "resourcepack",
        ResourceType.Shader => "shader",
        ResourceType.DataPack => "datapack",
        _ => "mod",
    };

    public static int ToCurseForgeClassId(this ResourceType type) => type switch
    {
        ResourceType.Mod => 6,
        ResourceType.ModPack => 4471,
        ResourceType.ResourcePack => 12,
        ResourceType.Shader => 6552,
        ResourceType.DataPack => 6945,
        _ => 6,
    };

    public static string GetTargetFolder(this ResourceType type) => type switch
    {
        ResourceType.Mod => "mods",
        ResourceType.ModPack => "downloads",
        ResourceType.ResourcePack => "resourcepacks",
        ResourceType.Shader => "shaderpacks",
        ResourceType.DataPack => "datapacks",
        _ => "mods",
    };
}
