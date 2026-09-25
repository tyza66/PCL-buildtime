namespace PCL.Avalonia.Services.Downloads;

public sealed record DownloadProgress(long Received, long? TotalLength)
{
    public double Fraction => TotalLength is > 0
        ? Math.Min(1, (double)Received / TotalLength.Value)
        : 0;
}
