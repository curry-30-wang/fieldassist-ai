using AsphaltPlantManager.Core.Receivables;
using AsphaltPlantManager.Infrastructure.Database;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Database;

public sealed class SqliteCustomerReceivableRepositoryTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"asphalt-receivables-{Guid.NewGuid():N}.db");
    private string _connectionString = null!;
    private SqliteCustomerReceivableRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
        await new DatabaseInitializer(_connectionString).InitializeAsync(CancellationToken.None);
        _repository = new SqliteCustomerReceivableRepository(_connectionString);
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Saves_updates_lists_and_deletes_one_customer_record()
    {
        var id = Guid.NewGuid();
        await _repository.UpsertAsync(new CustomerReceivable(id, "甲客户", 100m, 20m, new DateOnly(2026, 8, 31), "首笔", DateTimeOffset.UtcNow), CancellationToken.None);
        await _repository.UpsertAsync(new CustomerReceivable(Guid.NewGuid(), "乙客户", 50m, 0m, null, "", DateTimeOffset.UtcNow), CancellationToken.None);
        await _repository.UpsertAsync(new CustomerReceivable(id, "甲客户", 120m, 60m, null, "更新", DateTimeOffset.UtcNow), CancellationToken.None);
        await _repository.UpsertAsync(new CustomerReceivable(id, "甲客户改名", 120m, 60m, null, "改名", DateTimeOffset.UtcNow), CancellationToken.None);

        var all = await _repository.ListAsync(null, CancellationToken.None);
        all.Should().HaveCount(2);
        all.Single(item => item.CustomerName == "甲客户改名").RemainingAmount.Should().Be(60m);
        (await _repository.ListAsync("改名", CancellationToken.None)).Should().ContainSingle().Which.CustomerName.Should().Be("甲客户改名");
        (await _repository.DeleteAsync(id, CancellationToken.None)).Should().BeTrue();
        (await _repository.ListAsync(null, CancellationToken.None)).Should().ContainSingle().Which.CustomerName.Should().Be("乙客户");
    }
}
