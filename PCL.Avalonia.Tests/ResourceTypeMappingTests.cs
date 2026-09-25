using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class ResourceTypeMappingTests
{
    [Theory]
    [InlineData(ResourceType.Mod, "mod", 6, "mods")]
    [InlineData(ResourceType.ModPack, "modpack", 4471, "downloads")]
    [InlineData(ResourceType.ResourcePack, "resourcepack", 12, "resourcepacks")]
    [InlineData(ResourceType.Shader, "shader", 6552, "shaderpacks")]
    [InlineData(ResourceType.DataPack, "datapack", 6945, "datapacks")]
    public void Mapping_MatchesOriginalResourceSearcher(
        ResourceType type,
        string modrinthProjectType,
        int curseForgeClassId,
        string targetFolder)
    {
        Assert.Equal(modrinthProjectType, type.ToModrinthProjectType());
        Assert.Equal(curseForgeClassId, type.ToCurseForgeClassId());
        Assert.Equal(targetFolder, type.GetTargetFolder());
    }
}
