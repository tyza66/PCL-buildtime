using System.Diagnostics;

namespace PCL.Avalonia.Services.Platform;

public sealed class DefaultFolderOpener : IFolderOpener
{
    public void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            throw new DirectoryNotFoundException("文件夹不存在：" + path);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // 打不开资源管理器时保持界面可用。
        }
    }
}
