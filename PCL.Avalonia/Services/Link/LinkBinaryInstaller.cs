using System.IO.Compression;
using System.Runtime.InteropServices;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Link;

public sealed class LinkBinaryInstaller : ILinkBinaryInstaller
{
    public const int CurrentEasyTierVersion = 14;
    public const string DefaultReleaseTag = "v2.6.4";
    private const string CoreBinaryName = "easytier-core";
    private const string CliBinaryName = "easytier-cli";

    private readonly string _directory;
    private readonly IDownloadClient _downloadClient;
    private readonly Func<(string Platform, string Architecture)> _platformInfo;
    private readonly string _releaseTag;
    private readonly bool _includePacketDll;
    private readonly bool _isWindows;

    public LinkBinaryInstaller(
        string directory,
        IDownloadClient downloadClient,
        Func<(string Platform, string Architecture)>? platformInfo = null,
        string? releaseTag = null,
        bool? includePacketDll = null)
    {
        _directory = directory;
        _downloadClient = downloadClient;
        _platformInfo = platformInfo ?? DetectPlatform;
        var (platform, _) = _platformInfo();
        _isWindows = platform == "windows";
        _releaseTag = string.IsNullOrWhiteSpace(releaseTag) ? DefaultReleaseTag : releaseTag;
        _includePacketDll = includePacketDll ?? _isWindows;
    }

    public string CorePath => Path.Combine(_directory, "联机模块" + (_isWindows ? ".exe" : ""));

    public string CliPath => Path.Combine(_directory, "联机模块 CLI" + (_isWindows ? ".exe" : ""));

    private string PacketDllPath => Path.Combine(_directory, "Packet.dll");

    public async Task EnsureInstalledAsync(
        int installedVersion,
        Action<int> persistVersion,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        var binariesReady = File.Exists(CorePath) && File.Exists(CliPath)
            && (!_includePacketDll || File.Exists(PacketDllPath));
        if (binariesReady && installedVersion >= CurrentEasyTierVersion)
        {
            return;
        }

        status?.Report("正在下载联机模块……");
        var (platform, architecture) = _platformInfo();
        var assetName = BuildAssetName(platform, architecture, _releaseTag);
        var downloadUrl = BuildDownloadUrl(_releaseTag, assetName);
        var zipPath = Path.Combine(_directory, "EasyTier.zip");
        Directory.CreateDirectory(_directory);
        await _downloadClient.DownloadAsync(
            new DownloadRequest(downloadUrl, zipPath, "EasyTier.zip"),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        status?.Report("正在解压联机模块……");
        ExtractBinaries(zipPath);
        File.Delete(zipPath);
        persistVersion(CurrentEasyTierVersion);
    }

    private void ExtractBinaries(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var core = FindBinary(archive, CoreBinaryName)
            ?? throw new InvalidDataException("联机模块压缩包中未找到 easytier-core");
        var cli = FindBinary(archive, CliBinaryName)
            ?? throw new InvalidDataException("联机模块压缩包中未找到 easytier-cli");

        var temporaryDirectory = Path.Combine(_directory, "EasyTier.tmp");
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }

        Directory.CreateDirectory(temporaryDirectory);
        ExtractEntry(archive, core, Path.Combine(temporaryDirectory, CoreBinaryName));
        ExtractEntry(archive, cli, Path.Combine(temporaryDirectory, CliBinaryName));
        if (_includePacketDll)
        {
            var packet = archive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.Name, "Packet.dll", StringComparison.OrdinalIgnoreCase));
            if (packet is not null)
            {
                ExtractEntry(archive, packet, Path.Combine(temporaryDirectory, "Packet.dll"));
            }
        }

        var suffix = _isWindows ? ".exe" : "";
        File.Move(
            Path.Combine(temporaryDirectory, CoreBinaryName),
            CorePath,
            overwrite: true);
        File.Move(
            Path.Combine(temporaryDirectory, CliBinaryName),
            CliPath,
            overwrite: true);
        if (_includePacketDll)
        {
            var packetPath = Path.Combine(temporaryDirectory, "Packet.dll");
            if (File.Exists(packetPath))
            {
                File.Move(packetPath, PacketDllPath, overwrite: true);
            }
        }

        Directory.Delete(temporaryDirectory, recursive: true);
    }

    private static ZipArchiveEntry? FindBinary(ZipArchive archive, string binaryName)
    {
        return archive.Entries.FirstOrDefault(entry =>
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            var fileName = entry.Name;
            return string.Equals(fileName, binaryName + ".exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, binaryName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void ExtractEntry(ZipArchive archive, ZipArchiveEntry entry, string destination)
    {
        entry.ExtractToFile(destination, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                destination,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    public static string BuildAssetName(string platform, string architecture, string releaseTag)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            platform = architecture == "aarch64" ? "macos" : "linux";
        }

        return $"easytier-{platform}-{architecture}-{releaseTag}.zip";
    }

    public static string BuildDownloadUrl(string releaseTag, string assetName)
    {
        return $"https://github.com/EasyTier/EasyTier/releases/download/{releaseTag}/{assetName}";
    }

    private static (string Platform, string Architecture) DetectPlatform()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "i686",
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            Architecture.Arm => "arm",
            _ => "x86_64",
        };
        var platform = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsMacOS()
                ? "macos"
                : "linux";
        return (platform, architecture);
    }
}
