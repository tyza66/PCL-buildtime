using System.Text.Json;

namespace PCL.Avalonia.Services.Accounts;

public sealed class JsonAccountService : IAccountService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _accountsPath;

    public JsonAccountService(string accountsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountsPath);
        _accountsPath = accountsPath;
    }

    public IReadOnlyList<Account> Load()
    {
        var (accounts, _) = ReadStore();
        return accounts;
    }

    public Account AddOfflineAccount(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var (accounts, defaultId) = ReadStore();
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Type = "offline",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        accounts.Add(account);
        WriteStore(accounts, defaultId ?? account.Id);
        return account;
    }

    public void RemoveAccount(Guid id)
    {
        var (accounts, defaultId) = ReadStore();
        var removed = accounts.RemoveAll(account => account.Id == id) > 0;
        if (defaultId == id)
        {
            defaultId = accounts.FirstOrDefault()?.Id;
        }

        if (removed || defaultId == id)
        {
            WriteStore(accounts, defaultId);
        }
    }

    public void SetDefaultAccount(Guid id)
    {
        var (accounts, _) = ReadStore();
        if (accounts.All(account => account.Id != id))
        {
            return;
        }

        WriteStore(accounts, id);
    }

    public Account? GetDefaultAccount()
    {
        var (accounts, defaultId) = ReadStore();
        return defaultId is null
            ? null
            : accounts.FirstOrDefault(account => account.Id == defaultId);
    }

    private (List<Account> Accounts, Guid? DefaultId) ReadStore()
    {
        try
        {
            if (!File.Exists(_accountsPath))
            {
                return ([], null);
            }

            var store = JsonSerializer.Deserialize<AccountStore>(File.ReadAllText(_accountsPath), JsonOptions);
            return (store?.Accounts ?? [], store?.DefaultAccountId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ([], null);
        }
    }

    private void WriteStore(List<Account> accounts, Guid? defaultId)
    {
        var directory = Path.GetDirectoryName(_accountsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var store = new AccountStore
        {
            DefaultAccountId = defaultId,
            Accounts = accounts,
        };
        var tempPath = _accountsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(store, JsonOptions));
        File.Move(tempPath, _accountsPath, overwrite: true);
    }

    private sealed class AccountStore
    {
        public Guid? DefaultAccountId { get; init; }

        public List<Account>? Accounts { get; init; }
    }
}
