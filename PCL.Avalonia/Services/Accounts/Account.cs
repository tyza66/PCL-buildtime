namespace PCL.Avalonia.Services.Accounts;

public sealed class Account
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Type { get; set; } = "offline";

    public string? Uuid { get; set; }

    public string? AccessToken { get; set; }

    public string? RefreshToken { get; set; }

    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    public string? SkinUrl { get; set; }

    public string? CapeUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
