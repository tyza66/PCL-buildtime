namespace PCL.Avalonia.Services.Accounts;

public interface IMicrosoftAuthenticationService
{
    Task<MicrosoftAccountSession> LoginAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<MicrosoftAccountSession?> RefreshAsync(
        Account account,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
