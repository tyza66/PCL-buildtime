using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Services.Game;

public interface ILaunchScriptExporter
{
    string Export(LaunchPlan plan, string filePath);
}
