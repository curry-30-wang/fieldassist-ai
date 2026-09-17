using AsphaltPlantManager.Core.Dashboard;
using AsphaltPlantManager.Core.Records;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AsphaltPlantManager.App;

public partial class DashboardViewModel(DashboardService dashboard) : ObservableObject
{
    private readonly DashboardService _dashboard = dashboard;
    public ObservableCollection<FormRecord> RecentRecords { get; } = [];
    [ObservableProperty] private decimal _todayProduction;
    [ObservableProperty] private decimal _todayConsumption;
    [ObservableProperty] private decimal? _currentInventory;
    [ObservableProperty] private decimal _accountsReceivable;
    [ObservableProperty] private string _reminder = "暂无提醒";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var summary = await _dashboard.GetAsync(DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
        TodayProduction = summary.TodayProduction; TodayConsumption = summary.TodayAsphaltConsumption;
        CurrentInventory = summary.CurrentAsphaltInventory; AccountsReceivable = summary.AccountsReceivable;
        Reminder = summary.IsLowStock ? "沥青库存低于预警线" : summary.UpcomingMaintenanceRecords.Count > 0 ? "有待保养设备" : "暂无提醒";
        RecentRecords.Clear(); foreach (var record in summary.RecentRecords) RecentRecords.Add(record);
    }
}
