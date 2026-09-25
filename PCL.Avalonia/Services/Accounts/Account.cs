namespace PCL.Avalonia.Services.Accounts;

public sealed class Account
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Type { get; set; } = "offline";

    public DateTimeOffset CreatedAt { get; set; }
}
