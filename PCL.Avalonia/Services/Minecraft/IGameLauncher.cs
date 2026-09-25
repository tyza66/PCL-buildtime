namespace PCL.Avalonia.Services.Minecraft;

public interface IGameLauncher
{
    LaunchPlan BuildLaunchPlan(MinecraftVersion version, AppSettings settings, string javaExecutable);

    GameLaunch Launch(LaunchPlan plan, IProgress<string>? output = null);
}
