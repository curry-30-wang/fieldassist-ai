using AsphaltPlantManager.Core.Dashboard;
using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Search;
using FluentAssertions;
using Xunit;

namespace AsphaltPlantManager.Core.Tests.Dashboard;

public sealed class DashboardServiceTests
{
    private static readonly DateOnly Today = new(2026, 8, 7);

    [Fact]
    public async Task Calculates_only_the_explicit_v1_metrics_without_counting_other_templates_or_versions()
    {
        var production = Create("production-daily", Today, "{\"actualProduction\":100.5}", 1);
        var dispatch = Create("finished-goods-dispatch", Today, "{\"quantity\":999}", 2);
        var asphaltToday = Create("asphalt-inventory", Today, "{\"consumption\":12.25,\"closing\":18}", 3);
        var asphaltOld = Create("asphalt-inventory", Today.AddDays(-1), "{\"consumption\":99,\"closing\":50}", 4);
        var reconciliationOne = Create("customer-reconciliation", Today, "{\"receivable\":100}", 5);
        var reconciliationTwo = Create("customer-reconciliation", Today.AddDays(-1), "{\"receivable\":25.5}", 6);
        var deleted = Create("production-daily", Today, "{\"actualProduction\":900}", 7);
        await using var repository = new InMemoryRecordRepository([production, dispatch, asphaltToday, asphaltOld, reconciliationOne, reconciliationTwo], [deleted]);
        var service = new DashboardService(repository, new FixedSettingsReader(null));

        var summary = await service.GetAsync(Today, CancellationToken.None);

        summary.TodayProduction.Should().Be(100.5m);
        summary.TodayAsphaltConsumption.Should().Be(12.25m);
        summary.CurrentAsphaltInventory.Should().Be(18m);
        summary.TotalReceivable.Should().Be(125.5m);
        summary.IsAsphaltStockLow.Should().BeTrue();
        summary.LowStockThreshold.Should().Be(20m);
    }

    [Fact]
    public async Task Applies_configured_low_stock_threshold_and_returns_recent_records_and_maintenance_inclusive_boundaries()
    {
        var inventory = Create("asphalt-inventory", Today, "{\"closing\":18,\"consumption\":1}", 1);
        var dueToday = Create("equipment-maintenance", Today, "{\"nextMaintenanceDate\":\"2026-08-07\"}", 2);
        var dueInSevenDays = Create("equipment-maintenance", Today, "{\"nextMaintenanceDate\":\"2026-08-14\"}", 3);
        var dueLater = Create("equipment-maintenance", Today, "{\"nextMaintenanceDate\":\"2026-08-15\"}", 4);
        var records = new List<FormRecord> { inventory, dueToday, dueInSevenDays, dueLater };
        records.AddRange(Enumerable.Range(5, 10).Select(index => Create("quality-inspection", Today.AddDays(-index), "{}", index)));
        await using var repository = new InMemoryRecordRepository(records, []);
        var service = new DashboardService(repository, new FixedSettingsReader("15"));

        var summary = await service.GetAsync(Today, CancellationToken.None);

        summary.IsAsphaltStockLow.Should().BeFalse();
        summary.LowStockThreshold.Should().Be(15m);
        summary.RecentRecords.Should().HaveCount(10);
        summary.UpcomingMaintenanceRecords.Select(record => record.Id).Should().BeEquivalentTo(new[] { dueToday.Id, dueInSevenDays.Id });
    }

    [Fact]
    public async Task Reads_more_than_two_hundred_records_from_one_dashboard_snapshot()
    {
        var records = Enumerable.Range(1, 201).Select(index => Create("production-daily", Today, "{\"actualProduction\":1}", index % 50)).ToArray();
        var repository = new SnapshotOnlyRepository(records);
        var service = new DashboardService(repository, new FixedSettingsReader(null));

        var summary = await service.GetAsync(Today, CancellationToken.None);

        summary.TodayProduction.Should().Be(201m);
        repository.SnapshotCalls.Should().Be(1);
    }

    [Fact]
    public async Task Propagates_cancellation_to_the_dashboard_snapshot()
    {
        var repository = new SnapshotOnlyRepository([]);
        var service = new DashboardService(repository, new FixedSettingsReader(null));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var get = () => service.GetAsync(Today, cancellation.Token);

        await get.Should().ThrowAsync<OperationCanceledException>();
    }

    private static FormRecord Create(string templateId, DateOnly date, string payload, int sequence)
    {
        var record = FormRecord.CreateDraft(
            templateId, 1, date, date, new Dictionary<string, string>(), payload, templateId,
            DateTimeOffset.Parse($"2026-08-07T08:{sequence:00}:00+08:00"));
        record.MarkCompleted();
        return record;
    }

    private sealed class FixedSettingsReader(string? threshold) : ISettingsReader
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult(threshold);
    }

    private sealed class InMemoryRecordRepository(IReadOnlyList<FormRecord> active, IReadOnlyList<FormRecord> deleted) : IRecordRepository, IAsyncDisposable
    {
        private readonly IReadOnlyList<FormRecord> _active = active;

        public Task SaveAsync(FormRecord record, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<FormRecord?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_active.Concat(deleted).SingleOrDefault(record => record.Id == id));

        public Task<PagedResult> SearchAsync(RecordQuery query, CancellationToken cancellationToken)
        {
            var ordered = _active.OrderByDescending(record => record.PeriodStart).ThenByDescending(record => record.UpdatedAt).ThenBy(record => record.Id).ToArray();
            var items = ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(record => new SearchResult(record)).ToArray();
            return Task.FromResult(new PagedResult(items, ordered.Length, query.Page, query.PageSize));
        }

        public Task<IReadOnlyList<FormRecord>> GetActiveRecordsSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(_active);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SnapshotOnlyRepository(IReadOnlyList<FormRecord> records) : IRecordRepository
    {
        public int SnapshotCalls { get; private set; }

        public Task SaveAsync(FormRecord record, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<FormRecord?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<FormRecord?>(null);

        public Task<PagedResult> SearchAsync(RecordQuery query, CancellationToken cancellationToken) => throw new InvalidOperationException("Dashboard must not page through a changing search result.");

        public Task<IReadOnlyList<FormRecord>> GetActiveRecordsSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotCalls++;
            return Task.FromResult(records);
        }
    }
}
