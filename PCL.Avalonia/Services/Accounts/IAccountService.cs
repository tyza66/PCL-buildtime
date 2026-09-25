namespace PCL.Avalonia.Services.Accounts;

public interface IAccountService
{
    IReadOnlyList<Account> Load();

    Account AddOfflineAccount(string name);

    Account AddMicrosoftAccount(MicrosoftAccountSession session);

    void RemoveAccount(Guid id);

    void SetDefaultAccount(Guid id);

    Account? GetDefaultAccount();
}
