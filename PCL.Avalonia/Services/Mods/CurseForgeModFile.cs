namespace PCL.Avalonia.Services.Mods;

public sealed class CurseForgeModFile
{
    public int Id { get; init; }

    public string DisplayName { get; init; } = "";

    public string FileName { get; init; } = "";

    public string? DownloadUrl { get; init; }

    public long FileLength { get; init; }

    public List<CurseForgeFileHash> FileHashes { get; init; } = [];

    public DateTimeOffset? FileDate { get; init; }

    public string? Sha1
    {
        get
        {
            var hash = FileHashes.FirstOrDefault(item => item.Algo == 1) ?? FileHashes.FirstOrDefault();
            return hash?.Value;
        }
    }
}

public sealed class CurseForgeFileHash
{
    public int Algo { get; init; }

    public string? Value { get; init; }
}
