using AsphaltPlantManager.Core.Records;

namespace AsphaltPlantManager.Core.Dashboard;

public sealed class DashboardSummary
{
    public DashboardSummary(
        decimal todayProduction,
        decimal todayAsphaltConsumption,
        decimal? currentAsphaltInventory,
        decimal totalReceivable,
        IReadOnlyList<FormRecord> recentRecords,
        decimal lowStockThreshold,
        IReadOnlyList<FormRecord> upcomingMaintenanceRecords)
    {
        TodayProduction = todayProduction;
        TodayAsphaltConsumption = todayAsphaltConsumption;
        CurrentAsphaltInventory = currentAsphaltInventory;
        TotalReceivable = totalReceivable;
        RecentRecords = recentRecords ?? throw new ArgumentNullException(nameof(recentRecords));
        LowStockThreshold = lowStockThreshold;
        UpcomingMaintenanceRecords = upcomingMaintenanceRecords ?? throw new ArgumentNullException(nameof(upcomingMaintenanceRecords));
    }

    public decimal TodayProduction { get; }
    public decimal TodayAsphaltConsumption { get; }
    public decimal? CurrentAsphaltInventory { get; }
    public decimal TotalReceivable { get; }
    public decimal AccountsReceivable => TotalReceivable;
    public IReadOnlyList<FormRecord> RecentRecords { get; }
    public decimal LowStockThreshold { get; }
    public bool IsAsphaltStockLow => CurrentAsphaltInventory is decimal inventory && inventory < LowStockThreshold;
    public bool IsLowStock => IsAsphaltStockLow;
    public IReadOnlyList<FormRecord> UpcomingMaintenanceRecords { get; }
}
