namespace PCL.Avalonia.Services;

public interface IJavaListService
{
    IReadOnlyList<JavaInfo> Scan();

    JavaInfo? GetJava(string path);

    void Refresh();
}

public sealed record JavaInfo(
    string Path,
    string Version,
    string Architecture,
    bool IsValid);
