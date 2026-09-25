using PCL.Avalonia.Services.Accounts;

namespace PCL.Avalonia.Services.Minecraft;

public interface IGameLauncher
{
    LaunchPlan BuildLaunchPlan(
        MinecraftVersion version,
        AppSettings settings,
        string javaExecutable,
        Account? account = null);

    IGameLaunch Launch(LaunchPlan plan, IProgress<string>? output = null);
}
