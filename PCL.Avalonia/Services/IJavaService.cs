namespace PCL.Avalonia.Services;

public interface IJavaService
{
    string? ResolveJavaExecutable(AppSettings settings);

    // requiredMajorVersion 是版本清单要求的 Java 大版本号（如 26.3 需要 25），为 null 时退回默认选择。
    string? ResolveJavaExecutable(AppSettings settings, int? requiredMajorVersion);
}
