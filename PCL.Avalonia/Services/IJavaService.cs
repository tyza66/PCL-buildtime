namespace PCL.Avalonia.Services;

public interface IJavaService
{
    string? ResolveJavaExecutable(AppSettings settings);
}
