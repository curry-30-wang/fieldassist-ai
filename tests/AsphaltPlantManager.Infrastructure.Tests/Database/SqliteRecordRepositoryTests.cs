using AsphaltPlantManager.Core.MasterData;
using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Search;
using AsphaltPlantManager.Infrastructure.Database;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Database;

public sealed class SqliteRecordRepositoryTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"asphalt-{Guid.NewGuid():N}.db");
    private string _connectionString = null!;
    private SqliteRecordRepository _records = null!;
    private SqliteMasterDataRepository _masterData = null!;

    public async Task InitializeAsync()
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
        await new DatabaseInitializer(_connectionString).InitializeAsync(CancellationToken.None);
        _records = new SqliteRecordRepository(_connectionString);
        _masterData = new SqliteMasterDataRepository(_connectionString);
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Initializing_twice_creates_required_tables_and_records_one_migration()
    {
        await new DatabaseInitializer(_connectionString).InitializeAsync(CancellationToken.None);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        tables.Should().Contain(new[] { "schema_migrations", "records", "record_versions", "master_data", "settings", "archive_sequences", "trash", "customer_receivables" });

        await using var migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        ((long)(await migrationCommand.ExecuteScalarAsync())!).Should().Be(2);

        await using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA journal_mode;";
        ((string)(await pragmaCommand.ExecuteScalarAsync())!).Should().Be("wal");
    }

    [Fact]
    public async Task Master_data_can_list_and_delete_common_items()
    {
        await _masterData.UpsertAsync(new MasterDataItem("车辆", "沪A12345", "一号车"), CancellationToken.None);
        await _masterData.UpsertAsync(new MasterDataItem("车辆", "沪B67890", "二号车"), CancellationToken.None);

        var items = await _masterData.ListAsync("车辆", CancellationToken.None);

        items.Select(item => item.Key).Should().Equal("沪A12345", "沪B67890");
        (await _masterData.DeleteAsync("车辆", "沪A12345", CancellationToken.None)).Should().BeTrue();
        (await _masterData.ListAsync("车辆", CancellationToken.None)).Should().ContainSingle().Which.Key.Should().Be("沪B67890");
        (await _masterData.DeleteAsync("车辆", "沪A12345", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Saved_record_round_trips_metadata_and_unicode_json()
    {
        var record = CreateRecord("旧公司", "{\"quantity\":12.5,\"note\":\"中文不转义\"}");
        record.MarkCompleted();
        record.Seal("GHC-202608-0001", "首次封存", DateTimeOffset.Parse("2026-08-07T08:30:00+08:00"));

        await _records.SaveAsync(record, CancellationToken.None);

        var loaded = await _records.GetAsync(record.Id, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(record.Id);
        loaded.TemplateId.Should().Be("production-daily");
        loaded.TemplateVersion.Should().Be(3);
        loaded.PeriodStart.Should().Be(new DateOnly(2026, 8, 1));
        loaded.PeriodEnd.Should().Be(new DateOnly(2026, 8, 1));
        loaded.Status.Should().Be(RecordStatus.Sealed);
        loaded.ArchiveNumber.Should().Be("GHC-202608-0001");
        loaded.ChangeNote.Should().Be("首次封存");
        loaded.SealedAt.Should().Be(DateTimeOffset.Parse("2026-08-07T08:30:00+08:00"));
        loaded.HeaderSnapshot["company"].Should().Be("旧公司");
        loaded.PayloadJson.Should().Be("{\"quantity\":12.5,\"note\":\"中文不转义\"}");
        loaded.SearchText.Should().Be("旧公司 AC-13");
    }

    [Fact]
    public async Task Saved_record_keeps_company_snapshot_after_master_data_changes()
    {
        var record = CreateRecord("旧公司", "{}");
        await _records.SaveAsync(record, CancellationToken.None);
        await _masterData.UpsertAsync(new MasterDataItem("company", "default", "新公司"), CancellationToken.None);

        var loaded = await _records.GetAsync(record.Id, CancellationToken.None);

        loaded!.HeaderSnapshot["company"].Should().Be("旧公司");
        (await _masterData.GetAsync("company", "default", CancellationToken.None))!.Value.Should().Be("新公司");
    }

    [Fact]
    public async Task Saving_same_record_updates_it_without_creating_another_row()
    {
        var record = CreateRecord("旧公司", "{\"quantity\":10}");
        await _records.SaveAsync(record, CancellationToken.None);
        record.UpdateContent(new Dictionary<string, string> { ["company"] = "新公司" }, "{\"quantity\":20}", "新公司 AC-13", DateTimeOffset.Parse("2026-08-07T09:00:00+08:00"));

        await _records.SaveAsync(record, CancellationToken.None);

        var matches = await _records.SearchAsync(new RecordQuery(), CancellationToken.None);
        matches.Should().ContainSingle();
        matches[0].Record.PayloadJson.Should().Be("{\"quantity\":20}");
        matches[0].Record.HeaderSnapshot["company"].Should().Be("新公司");
    }

    [Fact]
    public async Task Saving_existing_record_does_not_overwrite_its_versions()
    {
        var record = CreateRecord("旧公司", "{\"quantity\":10}");
        await _records.SaveAsync(record, CancellationToken.None);
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO record_versions (record_id, version, payload_json, change_note, created_at) VALUES (@id, @version, @payload, @note, @created);";
            command.Parameters.AddWithValue("@id", record.Id.ToString("D"));
            command.Parameters.AddWithValue("@version", 1);
            command.Parameters.AddWithValue("@payload", "{\"quantity\":10}");
            command.Parameters.AddWithValue("@note", "首次封存");
            command.Parameters.AddWithValue("@created", "2026-08-07T08:00:00.0000000+08:00");
            await command.ExecuteNonQueryAsync();
        }

        record.UpdateContent(new Dictionary<string, string> { ["company"] = "新公司" }, "{\"quantity\":20}", "新公司 AC-13", DateTimeOffset.Parse("2026-08-07T09:00:00+08:00"));
        await _records.SaveAsync(record, CancellationToken.None);

        await using var verifyConnection = new SqliteConnection(_connectionString);
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = "SELECT payload_json FROM record_versions WHERE record_id = @id AND version = @version;";
        verify.Parameters.AddWithValue("@id", record.Id.ToString("D"));
        verify.Parameters.AddWithValue("@version", 1);
        ((string)(await verify.ExecuteScalarAsync())!).Should().Be("{\"quantity\":10}");
    }

    [Fact]
    public async Task Cancelled_save_does_not_create_a_partial_record()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var save = () => _records.SaveAsync(CreateRecord("旧公司", "{}"), cancellation.Token);

        await save.Should().ThrowAsync<OperationCanceledException>();
        (await _records.SearchAsync(new RecordQuery(), CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Loading_a_non_iso_offset_timestamp_is_rejected()
    {
        var record = CreateRecord("旧公司", "{}");
        await _records.SaveAsync(record, CancellationToken.None);
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE records SET updated_at = @updatedAt WHERE id = @id;";
            command.Parameters.AddWithValue("@updatedAt", "2026-08-07T08:00:00+08:00");
            command.Parameters.AddWithValue("@id", record.Id.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        Func<Task> load = async () => _ = await _records.GetAsync(record.Id, CancellationToken.None);

        await load.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task Loading_a_non_iso_date_is_rejected()
    {
        var record = CreateRecord("旧公司", "{}");
        await _records.SaveAsync(record, CancellationToken.None);
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE records SET period_start = @periodStart WHERE id = @id;";
            command.Parameters.AddWithValue("@periodStart", "2026/08/07");
            command.Parameters.AddWithValue("@id", record.Id.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        Func<Task> load = async () => _ = await _records.GetAsync(record.Id, CancellationToken.None);

        await load.Should().ThrowAsync<FormatException>();
    }

    private static FormRecord CreateRecord(string company, string payload) => FormRecord.CreateDraft(
        "production-daily",
        3,
        new DateOnly(2026, 8, 1),
        new DateOnly(2026, 8, 1),
        new Dictionary<string, string> { ["company"] = company, ["customer"] = "甲方" },
        payload,
        $"{company} AC-13",
        DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"));
}
