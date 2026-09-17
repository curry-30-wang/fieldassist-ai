using AsphaltPlantManager.Core.Backup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsphaltPlantManager.App;

public partial class BackupViewModel(IBackupService backup) : ObservableObject
{
    private readonly IBackupService _backup = backup;
    [ObservableProperty] private string _destinationRoot = backup.DefaultDestinationRoot;
    [ObservableProperty] private string _backupPath = "";
    [ObservableProperty] private bool _restoreConfirmed;
    [ObservableProperty] private string _message = "恢复会覆盖当前本机数据；请先手动创建备份。";

    [RelayCommand]
    private async Task CreateAsync()
    {
        try { BackupPath = await _backup.CreateAsync(BackupReason.Manual, DestinationRoot, CancellationToken.None); Message = $"备份已创建：{BackupPath}"; }
        catch (Exception exception) { Message = $"创建备份失败：{exception.Message}"; }
    }

    [RelayCommand]
    private async Task ValidateAsync()
    {
        try { var checkedBackup = await _backup.ValidateAsync(BackupPath, CancellationToken.None); Message = checkedBackup.IsValid ? "备份验证通过，可以恢复。" : "备份无效，不能恢复。"; }
        catch (Exception exception) { Message = $"验证失败：{exception.Message}"; }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (!RestoreConfirmed) { Message = "请勾选“我已理解恢复会覆盖当前数据”后再恢复。"; return; }
        try { await _backup.RestoreAsync(BackupPath, CancellationToken.None); Message = "恢复完成，请重新打开应用以加载恢复后的数据。"; }
        catch (Exception exception) { Message = $"恢复失败：{exception.Message}"; }
    }
}
