using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class AccountsPageViewModel : ObservableObject
{
    private readonly IAccountService _accountService;
    private readonly SessionState _session;

    public AccountsPageViewModel(IAccountService accountService, SessionState session)
    {
        _accountService = accountService;
        _session = session;
        foreach (var account in accountService.Load())
        {
            Accounts.Add(new AccountItemViewModel(account, SetDefaultAsync, RemoveAsync));
        }

        SyncDefault();
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];

    [ObservableProperty]
    private string _newAccountName = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [RelayCommand]
    private async Task AddAsync()
    {
        var name = NewAccountName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "请输入离线账号名";
            return;
        }

        var account = await Task.Run(() => _accountService.AddOfflineAccount(name));
        Accounts.Add(new AccountItemViewModel(account, SetDefaultAsync, RemoveAsync));
        NewAccountName = "";
        SyncDefault();
        StatusMessage = $"已添加账号 {account.Name}";
    }

    private async Task SetDefaultAsync(AccountItemViewModel item)
    {
        await Task.Run(() => _accountService.SetDefaultAccount(item.Account.Id));
        SyncDefault();
        StatusMessage = $"已将 {item.Account.Name} 设为默认";
    }

    private async Task RemoveAsync(AccountItemViewModel item)
    {
        await Task.Run(() => _accountService.RemoveAccount(item.Account.Id));
        Accounts.Remove(item);
        SyncDefault();
        StatusMessage = $"已删除账号 {item.Account.Name}";
    }

    private void SyncDefault()
    {
        var defaultAccount = _accountService.GetDefaultAccount();
        _session.SelectedAccount = defaultAccount;
        foreach (var item in Accounts)
        {
            item.IsDefault = item.Account.Id == defaultAccount?.Id;
        }
    }
}

public sealed partial class AccountItemViewModel : ObservableObject
{
    private readonly Func<AccountItemViewModel, Task> _setDefault;
    private readonly Func<AccountItemViewModel, Task> _remove;

    public AccountItemViewModel(
        Account account,
        Func<AccountItemViewModel, Task> setDefault,
        Func<AccountItemViewModel, Task> remove)
    {
        Account = account;
        Name = account.Name;
        TypeText = account.Type == "offline" ? "离线账号" : account.Type;
        CreatedAtText = account.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        _setDefault = setDefault;
        _remove = remove;
    }

    public Account Account { get; }

    public string Name { get; }

    public string TypeText { get; }

    public string CreatedAtText { get; }

    public string DefaultBadgeText => IsDefault ? "默认" : "";

    public string SetDefaultButtonText => IsDefault ? "默认账号" : "设为默认";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultBadgeText))]
    [NotifyPropertyChangedFor(nameof(SetDefaultButtonText))]
    [NotifyCanExecuteChangedFor(nameof(SetDefaultCommand))]
    private bool _isDefault;

    [RelayCommand(CanExecute = nameof(CanSetDefault))]
    private Task SetDefault() => _setDefault(this);

    [RelayCommand]
    private Task Remove() => _remove(this);

    private bool CanSetDefault => !IsDefault;
}
