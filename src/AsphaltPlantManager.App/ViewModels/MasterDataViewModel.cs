using AsphaltPlantManager.Core.MasterData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;

namespace AsphaltPlantManager.App;

public partial class MasterDataViewModel : ObservableObject
{
    private readonly IMasterDataRepository _repository;

    public MasterDataViewModel(IMasterDataRepository repository) => _repository = repository;

    public ObservableCollection<string> Categories { get; } = ["公司", "客户", "工程", "规格", "配合比", "供应商", "车辆", "司机"];
    public ObservableCollection<MasterDataItem> Items { get; } = [];
    [ObservableProperty] private string _category = "客户";
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private MasterDataItem? _selectedItem;
    [ObservableProperty] private string _message = "输入名称和值后保存；基础资料可随时覆盖维护。";

    partial void OnCategoryChanged(string value) => _ = RefreshItemsAsync();

    partial void OnSelectedItemChanged(MasterDataItem? value)
    {
        if (value is null) return;
        Key = value.Key;
        Value = value.Value;
    }

    public async Task LoadAsync() => await RefreshItemsAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(Value)) { Message = "名称和值均不能为空。"; return; }
        try
        {
            await _repository.UpsertAsync(new MasterDataItem(Category.Trim(), Key.Trim(), Value.Trim()), CancellationToken.None);
            await RefreshItemsAsync().ConfigureAwait(true);
            Message = "基础资料已保存。";
        }
        catch (Exception exception) { Message = $"保存失败：{exception.Message}"; }
    }

    [RelayCommand]
    private async Task LoadItemAsync()
    {
        if (string.IsNullOrWhiteSpace(Key)) { Message = "请输入要查找的名称。"; return; }
        var item = await _repository.GetAsync(Category.Trim(), Key.Trim(), CancellationToken.None);
        Value = item?.Value ?? "";
        Message = item is null ? "未找到该基础资料。" : "已加载基础资料。";
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (string.IsNullOrWhiteSpace(Key)) { Message = "请先选择要删除的资料。"; return; }
        if (MessageBox.Show($"确定删除“{Key.Trim()}”吗？", "删除基础资料", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var deleted = await _repository.DeleteAsync(Category.Trim(), Key.Trim(), CancellationToken.None);
            await RefreshItemsAsync().ConfigureAwait(true);
            SelectedItem = null;
            Message = deleted ? "基础资料已删除。" : "没有找到要删除的资料。";
        }
        catch (Exception exception) { Message = $"删除失败：{exception.Message}"; }
    }

    private async Task RefreshItemsAsync()
    {
        if (string.IsNullOrWhiteSpace(Category)) return;
        Items.Clear();
        foreach (var item in await _repository.ListAsync(Category.Trim(), CancellationToken.None).ConfigureAwait(true)) Items.Add(item);
    }
}
