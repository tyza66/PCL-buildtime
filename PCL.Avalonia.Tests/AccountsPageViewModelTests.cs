using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class AccountsPageViewModelTests
{
    private sealed class FakeAccountService : IAccountService
    {
        public List<Account> Accounts { get; } = [];

        public Guid? DefaultAccountId { get; private set; }

        public List<string> AddedNames { get; } = [];

        public List<Guid> RemovedIds { get; } = [];

        public List<Guid> DefaultCalls { get; } = [];

        public FakeAccountService(params Account[] accounts)
        {
            Accounts.AddRange(accounts);
            DefaultAccountId = accounts.FirstOrDefault()?.Id;
        }

        public IReadOnlyList<Account> Load() => Accounts.ToList();

        public Account AddOfflineAccount(string name)
        {
            AddedNames.Add(name);
            var account = new Account { Id = Guid.NewGuid(), Name = name };
            Accounts.Add(account);
            DefaultAccountId ??= account.Id;
            return account;
        }

        public void RemoveAccount(Guid id)
        {
            RemovedIds.Add(id);
            Accounts.RemoveAll(account => account.Id == id);
            if (DefaultAccountId == id)
            {
                DefaultAccountId = Accounts.FirstOrDefault()?.Id;
            }
        }

        public void SetDefaultAccount(Guid id)
        {
            DefaultCalls.Add(id);
            DefaultAccountId = id;
        }

        public Account? GetDefaultAccount()
            => Accounts.FirstOrDefault(account => account.Id == DefaultAccountId);
    }

    private static Account Account(string name, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        CreatedAt = DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
    };

    [Fact]
    public void Constructor_LoadsAccounts_AndSetsSessionDefault()
    {
        var first = Account("Alex");
        var second = Account("Steve");
        var service = new FakeAccountService(first, second);
        var session = new SessionState();

        var viewModel = new AccountsPageViewModel(service, session);

        Assert.Equal(2, viewModel.Accounts.Count);
        Assert.True(viewModel.Accounts[0].IsDefault);
        Assert.Equal(first.Id, session.SelectedAccount?.Id);
    }

    [Fact]
    public async Task AddCommand_AddsOfflineAccount()
    {
        var service = new FakeAccountService();
        var viewModel = new AccountsPageViewModel(service, new SessionState());
        viewModel.NewAccountName = "NewPlayer";

        await viewModel.AddCommand.ExecuteAsync(null);

        Assert.Equal(["NewPlayer"], service.AddedNames);
        Assert.Single(viewModel.Accounts);
        Assert.Contains("已添加", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SetDefaultCommand_SwitchesDefaultAccount()
    {
        var first = Account("Alex");
        var second = Account("Steve");
        var service = new FakeAccountService(first, second);
        var viewModel = new AccountsPageViewModel(service, new SessionState());
        var target = viewModel.Accounts[1];

        await target.SetDefaultCommand.ExecuteAsync(null);

        Assert.Equal([second.Id], service.DefaultCalls);
        Assert.True(target.IsDefault);
        Assert.False(viewModel.Accounts[0].IsDefault);
    }

    [Fact]
    public async Task RemoveCommand_RemovesAccount_AndSyncsSession()
    {
        var first = Account("Alex");
        var second = Account("Steve");
        var service = new FakeAccountService(first, second);
        var session = new SessionState();
        var viewModel = new AccountsPageViewModel(service, session);

        await viewModel.Accounts[0].RemoveCommand.ExecuteAsync(null);

        Assert.Equal([first.Id], service.RemovedIds);
        Assert.Single(viewModel.Accounts);
        Assert.Equal(second.Id, session.SelectedAccount?.Id);
    }
}
