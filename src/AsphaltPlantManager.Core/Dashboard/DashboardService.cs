using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Search;
using System.Globalization;
using System.Text.Json;

namespace AsphaltPlantManager.Core.Dashboard;

public sealed class DashboardService
{
    private const string LowStockThresholdKey = "asphalt_low_stock_threshold";
    private const decimal DefaultLowStockThreshold = 20m;
    private readonly IRecordRepository _records;
    private readonly ISettingsReader _settings;

    public DashboardService(IRecordRepository records, ISettingsReader settings)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<DashboardSummary> GetAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var records = await _records.GetActiveRecordsSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var ordered = records.OrderByDescending(record => record.PeriodStart).ThenByDescending(record => record.UpdatedAt).ThenBy(record => record.Id).ToArray();
        var threshold = await GetThresholdAsync(cancellationToken).ConfigureAwait(false);
        var todayRecords = ordered.Where(record => record.PeriodStart <= today && record.PeriodEnd >= today).ToArray();
        var todayProduction = todayRecords.Where(record => record.TemplateId == "production-daily").Sum(record => GetDecimal(record.PayloadJson, "actualProduction") ?? 0m);
        var todayConsumption = todayRecords.Where(record => record.TemplateId == "asphalt-inventory").Sum(record => GetDecimal(record.PayloadJson, "consumption") ?? 0m);
        var latestInventory = ordered.FirstOrDefault(record => record.TemplateId == "asphalt-inventory");
        var inventory = latestInventory is null ? null : GetDecimal(latestInventory.PayloadJson, "closing");
        var receivable = ordered.Where(record => record.TemplateId == "customer-reconciliation").Sum(record => GetDecimal(record.PayloadJson, "receivable") ?? 0m);
        var lastMaintenanceDay = today.AddDays(7);
        var upcomingMaintenance = ordered
            .Where(record => record.TemplateId == "equipment-maintenance")
            .Where(record => GetDate(record.PayloadJson, "nextMaintenanceDate") is DateOnly date && date >= today && date <= lastMaintenanceDay)
            .ToArray();

        return new DashboardSummary(todayProduction, todayConsumption, inventory, receivable, ordered.Take(10).ToArray(), threshold, upcomingMaintenance);
    }

    private async Task<decimal> GetThresholdAsync(CancellationToken cancellationToken)
    {
        var configured = await _settings.GetAsync(LowStockThresholdKey, cancellationToken).ConfigureAwait(false);
        return decimal.TryParse(configured, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold) ? threshold : DefaultLowStockThreshold;
    }

    private static decimal? GetDecimal(string payloadJson, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(property, out var value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
            return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateOnly? GetDate(string payloadJson, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return null;
            return DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
