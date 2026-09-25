using PCL.Avalonia.Services.Accounts;

namespace PCL.Avalonia.Tests;

public sealed class AccountServiceTests : IDisposable
{
    private readonly string _path;

    public AccountServiceTests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaAccounts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "accounts.json");
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(new JsonAccountService(_path).Load());
    }

    [Fact]
    public void AddOfflineAccount_CreatesPersists_AndBecomesDefault()
    {
        var service = new JsonAccountService(_path);

        var account = service.AddOfflineAccount("Steve");

        Assert.Equal("Steve", account.Name);
        Assert.Equal("offline", account.Type);
        Assert.Equal(account.Id, service.GetDefaultAccount()?.Id);

        var reloaded = new JsonAccountService(_path);
        var loaded = Assert.Single(reloaded.Load());
        Assert.Equal(account.Id, loaded.Id);
    }

    [Fact]
    public void RemoveDefaultAccount_FallsBackToRemaining()
    {
        var service = new JsonAccountService(_path);
        var first = service.AddOfflineAccount("Alex");
        var second = service.AddOfflineAccount("Steve");

        service.RemoveAccount(first.Id);

        Assert.Equal(second.Id, service.GetDefaultAccount()?.Id);
        Assert.Single(service.Load());
    }

    [Fact]
    public void SetDefaultAccount_PersistsChoice()
    {
        var service = new JsonAccountService(_path);
        var first = service.AddOfflineAccount("Alex");
        var second = service.AddOfflineAccount("Steve");

        service.SetDefaultAccount(second.Id);

        Assert.Equal(second.Id, service.GetDefaultAccount()?.Id);
        var reloaded = new JsonAccountService(_path);
        Assert.Equal(second.Id, reloaded.GetDefaultAccount()?.Id);
    }
}
