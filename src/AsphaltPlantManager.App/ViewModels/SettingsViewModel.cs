using AsphaltPlantManager.Core.MasterData;
using AsphaltPlantManager.Core.Records;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsphaltPlantManager.App;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IMasterDataRepository _masterData;
    private readonly IUnlockPasswordStore _passwords;
    [ObservableProperty] private string _companyName = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _message = "可设置公司名称及本次会话解锁密码。";

    public SettingsViewModel(IMasterDataRepository masterData, IUnlockPasswordStore passwords)
    {
        _masterData = masterData;
        _passwords = passwords;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        try { CompanyName = (await _masterData.GetAsync("公司", "default", CancellationToken.None).ConfigureAwait(false))?.Value ?? string.Empty; }
        catch { /* 首次启动没有公司名称时保持空白。 */ }
    }

    [RelayCommand]
    private async Task SaveCompanyAsync()
    {
        if (string.IsNullOrWhiteSpace(CompanyName)) { Message = "公司名称不能为空。"; return; }
        await _masterData.UpsertAsync(new MasterDataItem("公司", "default", CompanyName.Trim()), CancellationToken.None);
        Message = "公司名称已保存。";
    }

    [RelayCommand]
    private async Task SetPasswordAsync()
    {
        if (NewPassword.Length < 4) { Message = "解锁密码至少需要 4 个字符。"; return; }
        await _passwords.SetPasswordAsync(NewPassword, CancellationToken.None);
        NewPassword = ""; Message = "解锁密码已更新；它仅用于本次会话编辑已封存档案。";
    }
}
