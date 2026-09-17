using AsphaltPlantManager.Core.Receivables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Diagnostics;

namespace AsphaltPlantManager.App;

public partial class ReceivablesViewModel : ObservableObject
{
    private readonly CustomerReceivableService _service;
    private readonly ICustomerReceivableExporter _exporter;

    public ReceivablesViewModel(CustomerReceivableService service, ICustomerReceivableExporter exporter) { _service = service; _exporter = exporter; }

    public ObservableCollection<CustomerReceivable> Items { get; } = [];
    [ObservableProperty] private CustomerReceivable? _selectedItem;
    [ObservableProperty] private string _searchCustomer = "";
    [ObservableProperty] private string _customerName = "";
    [ObservableProperty] private string _receivableAmount = "0.00";
    [ObservableProperty] private string _paidAmount = "0.00";
    [ObservableProperty] private DateTime? _dueDate;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _message = "这里单独登记客户欠款，不需要录入每次生产。";

    public decimal TotalRemaining => Items.Sum(item => item.RemainingAmount);

    partial void OnSelectedItemChanged(CustomerReceivable? value)
    {
        if (value is null) return;
        CustomerName = value.CustomerName;
        ReceivableAmount = value.ReceivableAmount.ToString("N2", CultureInfo.CurrentCulture);
        PaidAmount = value.PaidAmount.ToString("N2", CultureInfo.CurrentCulture);
        DueDate = value.DueDate?.ToDateTime(TimeOnly.MinValue);
        Note = value.Note;
    }

    public async Task LoadAsync() => await SearchAsync().ConfigureAwait(true);

    [RelayCommand]
    public async Task SearchAsync()
    {
        try
        {
            Items.Clear();
            foreach (var item in await _service.ListAsync(SearchCustomer, CancellationToken.None).ConfigureAwait(true)) Items.Add(item);
            OnPropertyChanged(nameof(TotalRemaining));
            Message = $"共 {Items.Count} 个客户，剩余欠款 {TotalRemaining:N2} 元。";
        }
        catch (Exception exception) { Message = $"读取欠款失败：{exception.Message}"; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomerName)) { Message = "客户名称不能为空。"; return; }
        if (!TryParseAmount(ReceivableAmount, out var receivable) || !TryParseAmount(PaidAmount, out var paid)) { Message = "应收金额和已收金额请输入数字。"; return; }
        if (receivable < 0 || paid < 0) { Message = "金额不能小于 0。"; return; }
        try
        {
            var current = SelectedItem;
            var record = new CustomerReceivable(current?.Id ?? Guid.NewGuid(), CustomerName, receivable, paid,
                DueDate is null ? null : DateOnly.FromDateTime(DueDate.Value), Note, DateTimeOffset.Now);
            await _service.SaveAsync(record, CancellationToken.None);
            await SearchAsync().ConfigureAwait(true);
            SelectedItem = Items.FirstOrDefault(item => string.Equals(item.CustomerName, record.CustomerName, StringComparison.OrdinalIgnoreCase));
            Message = "客户欠款已保存。";
        }
        catch (Exception exception) { Message = $"保存欠款失败：{exception.Message}"; }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedItem is null) { Message = "请先选择要删除的客户。"; return; }
        if (MessageBox.Show($"确定删除“{SelectedItem.CustomerName}”的欠款记录吗？", "删除客户欠款", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _service.DeleteAsync(SelectedItem.Id, CancellationToken.None);
            SelectedItem = null;
            await SearchAsync().ConfigureAwait(true);
            Message = "客户欠款记录已删除。";
        }
        catch (Exception exception) { Message = $"删除欠款失败：{exception.Message}"; }
    }

    [RelayCommand]
    private void ClearEditor()
    {
        SelectedItem = null;
        CustomerName = string.Empty;
        ReceivableAmount = "0.00";
        PaidAmount = "0.00";
        DueDate = null;
        Note = string.Empty;
        Message = "可以录入新的客户欠款。";
    }

    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        if (SelectedItem is null) { Message = "请先选择要导出的客户。"; return; }
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "沥青拌合站管理系统", "客户欠款");
            var path = await _exporter.ExportAsync(SelectedItem, directory, CancellationToken.None).ConfigureAwait(true);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            Message = $"已打开客户欠款 Excel：{path}";
        }
        catch (Exception exception) { Message = $"导出欠款失败：{exception.Message}"; }
    }

    private static bool TryParseAmount(string value, out decimal amount) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
}
