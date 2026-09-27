namespace PCL.Avalonia.Services;

/// <summary>
/// 启动器自己下载安装的 JDK 统一落在配置目录的 java 子目录。
/// 安装服务往里写、Java 列表服务从这里扫，两边必须认同同一个位置：
/// 否则用户一键装完，设置页列表里还是不显示，安装按钮也不会收起来。
/// </summary>
public static class LauncherJavaPaths
{
    public const string FolderName = "java";

    public static string Root(IPlatformService platformService)
        => Path.Combine(platformService.GetConfigDirectory(), FolderName);
}
