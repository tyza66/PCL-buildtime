namespace PCL.Avalonia.Services.Mods;

public interface IModsService
{
    IReadOnlyList<ModInfo> Scan(string minecraftFolder);

    ModInfo SetEnabled(ModInfo mod, bool enabled);

    void Delete(ModInfo mod);
}
