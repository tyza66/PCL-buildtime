namespace PCL.Avalonia.Services.Minecraft;

public interface IForgelikeInstallRunner
{
    Task RunAsync(
        string minecraftFolder,
        string installerPath,
        ForgelikeKind kind,
        CancellationToken cancellationToken = default);
}
