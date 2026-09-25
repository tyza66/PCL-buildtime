namespace PCL.Avalonia.Services.Accounts;

public interface IAccountService
{
    IReadOnlyList<Account> Load();

    Account AddOfflineAccount(string name);

    void RemoveAccount(Guid id);

    void SetDefaultAccount(Guid id);

    Account? GetDefaultAccount();
}
