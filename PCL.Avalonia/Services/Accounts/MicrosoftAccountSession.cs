namespace PCL.Avalonia.Services.Accounts;

public sealed record MicrosoftAccountSession
{
    public required string Name { get; init; }

    public required string Uuid { get; init; }

    public required string AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public DateTimeOffset AccessTokenExpiresAt { get; init; }

    public string? SkinUrl { get; init; }

    public string? CapeUrl { get; init; }
}
