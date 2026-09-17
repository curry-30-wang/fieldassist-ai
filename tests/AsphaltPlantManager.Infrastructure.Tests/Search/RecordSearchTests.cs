using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Search;
using AsphaltPlantManager.Infrastructure.Database;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Search;

public sealed class RecordSearchTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"asphalt-search-{Guid.NewGuid():N}.db");
    private string _connectionString = null!;
    private SqliteRecordRepository _repository = null!;
    private FormRecord _matching = null!;
    private FormRecord _deleted = null!;

    public async Task InitializeAsync()
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _databasePath, Pooling = false }.ToString();
        await new DatabaseInitializer(_connectionString).InitializeAsync(CancellationToken.None);
        _repository = new SqliteRecordRepository(_connectionString);

        _matching = await SaveAsync("production-daily", new DateOnly(2026, 8, 10), "甲公司", "甲工程", "AC-13", "{\"receivable\":120,\"specification\":\"AC-13\",\"note\":\"唯一命中\"}", "甲公司 甲工程 AC-13 唯一命中", RecordStatus.Completed, 10);
        await SaveAsync("production-daily", new DateOnly(2026, 8, 11), "乙公司", "乙工程", "AC-13", "{\"receivable\":0,\"specification\":\"AC-13\"}", "乙公司 乙工程 AC-13", RecordStatus.Draft, 11);
        await SaveAsync("customer-reconciliation", new DateOnly(2026, 8, 12), "甲公司", "甲工程", "AC-16", "{\"receivable\":200,\"projectPart\":\"甲工程\",\"specification\":\"AC-16\"}", "甲公司 甲工程 AC-16", RecordStatus.Completed, 12);
        await SaveAsync("production-daily", new DateOnly(2026, 7, 31), "甲公司", "甲工程", "AC-13", "{\"receivable\":50,\"specification\":\"AC-13\"}", "七月 AC-13", RecordStatus.Completed, 13);
        await SaveAsync("production-daily", new DateOnly(2026, 9, 1), "丙公司", "丙工程", "SMA-13", "{\"receivable\":-1,\"specification\":\"SMA-13\"}", "九月 SMA-13", RecordStatus.Completed, 14);
        await SaveAsync("production-daily", new DateOnly(2026, 8, 13), "载荷客户", "载荷工程", "AC-20", "{\"customer\":\"载荷客户\",\"projectPart\":\"载荷工程\",\"specification\":\"AC-20\",\"receivable\":0}", "载荷字段", RecordStatus.Completed, 15, includeHeaderFields: false);
        _deleted = await SaveAsync("production-daily", new DateOnly(2026, 8, 14), "甲公司", "删除工程", "AC-13", "{\"receivable\":1,\"specification\":\"AC-13\"}", "已删除", RecordStatus.Completed, 16);
        await new SqliteArchiveRepository(_connectionString).MoveToTrashAsync(_deleted.Id, DateTimeOffset.Parse("2026-08-16T08:00:00+08:00"), CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Combined_filters_return_only_the_matching_active_record()
    {
        var result = await _repository.SearchAsync(new RecordQuery(
            From: new DateOnly(2026, 8, 1), To: new DateOnly(2026, 8, 31), TemplateId: "production-daily",
            Customer: "甲公司", Specification: "AC-13", HasReceivable: true), CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle().Which.Record.Id.Should().Be(_matching.Id);
    }

    [Fact]
    public async Task Filters_dates_inclusively_and_reads_project_specification_from_header_then_payload()
    {
        var august = await _repository.SearchAsync(new RecordQuery(From: new DateOnly(2026, 8, 1), To: new DateOnly(2026, 8, 31)), CancellationToken.None);
        var payloadFallback = await _repository.SearchAsync(new RecordQuery(Customer: "载荷客户", Project: "载荷工程", Specification: "AC-20"), CancellationToken.None);
        var status = await _repository.SearchAsync(new RecordQuery(Status: RecordStatus.Draft), CancellationToken.None);

        august.Items.Select(item => item.Record.Id).Should().NotContain(_deleted.Id);
        august.TotalCount.Should().Be(4);
        payloadFallback.Items.Should().ContainSingle();
        status.Items.Should().ContainSingle().Which.Record.Status.Should().Be(RecordStatus.Draft);
    }

    [Fact]
    public async Task Supports_receivable_keyword_deleted_exclusion_pagination_and_stable_ordering()
    {
        var receivable = await _repository.SearchAsync(new RecordQuery(HasReceivable: true), CancellationToken.None);
        var noReceivable = await _repository.SearchAsync(new RecordQuery(HasReceivable: false), CancellationToken.None);
        var keyword = await _repository.SearchAsync(new RecordQuery(Keyword: "唯一命中"), CancellationToken.None);
        var firstPage = await _repository.SearchAsync(new RecordQuery(Page: 1, PageSize: 2), CancellationToken.None);
        var secondPage = await _repository.SearchAsync(new RecordQuery(Page: 2, PageSize: 2), CancellationToken.None);

        receivable.Items.Should().NotContain(item => item.Record.Id == _deleted.Id);
        noReceivable.Items.Should().OnlyContain(item => item.Record.PayloadJson.Contains("\"receivable\":0", StringComparison.Ordinal) || item.Record.PayloadJson.Contains("\"receivable\":-", StringComparison.Ordinal));
        keyword.Items.Should().ContainSingle().Which.Record.Id.Should().Be(_matching.Id);
        firstPage.TotalCount.Should().Be(6);
        firstPage.Items.Should().HaveCount(2);
        secondPage.Items.Should().HaveCount(2);
        firstPage.Items.Select(item => item.Record.Id).Intersect(secondPage.Items.Select(item => item.Record.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Treats_only_integer_and_real_receivables_as_positive_receivables()
    {
        var boolean = await SaveAsync("customer-reconciliation", new DateOnly(2026, 8, 21), "boolean", "p", "AC-13", "{\"receivable\":true}", "boolean receivable", RecordStatus.Completed, 21);
        var numericString = await SaveAsync("customer-reconciliation", new DateOnly(2026, 8, 22), "string", "p", "AC-13", "{\"receivable\":\"100\"}", "string receivable", RecordStatus.Completed, 22);
        var positive = await _repository.SearchAsync(new RecordQuery(HasReceivable: true), CancellationToken.None);
        var nonPositive = await _repository.SearchAsync(new RecordQuery(HasReceivable: false), CancellationToken.None);

        positive.Items.Select(item => item.Record.Id).Should().NotContain(new[] { boolean.Id, numericString.Id });
        nonPositive.Items.Select(item => item.Record.Id).Should().Contain(new[] { boolean.Id, numericString.Id });
    }

    [Fact]
    public async Task Does_not_overflow_a_very_large_page_offset_back_to_the_first_page()
    {
        var result = await _repository.SearchAsync(new RecordQuery(Page: int.MaxValue, PageSize: 200), CancellationToken.None);

        result.TotalCount.Should().BeGreaterThan(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Orders_equal_period_records_by_utc_updated_time_then_id()
    {
        var earlierUtc = await SaveAsync("utc-order", new DateOnly(2026, 8, 23), "earlier", "p", "AC-13", "{}", "utc order", RecordStatus.Completed, 23);
        var laterUtc = await SaveAsync("utc-order", new DateOnly(2026, 8, 23), "later", "p", "AC-13", "{}", "utc order", RecordStatus.Completed, 24);
        var tieOne = await SaveAsync("utc-order", new DateOnly(2026, 8, 23), "tie-one", "p", "AC-13", "{}", "utc order", RecordStatus.Completed, 25);
        var tieTwo = await SaveAsync("utc-order", new DateOnly(2026, 8, 23), "tie-two", "p", "AC-13", "{}", "utc order", RecordStatus.Completed, 26);
        await UpdateTimestampAsync(earlierUtc.Id, "2026-08-23T08:00:00.0000000+08:00");
        await UpdateTimestampAsync(laterUtc.Id, "2026-08-23T01:00:00.0000000+00:00");
        await UpdateTimestampAsync(tieOne.Id, "2026-08-22T00:00:00.0000000+00:00");
        await UpdateTimestampAsync(tieTwo.Id, "2026-08-22T00:00:00.0000000+00:00");

        var result = await _repository.SearchAsync(new RecordQuery(From: new DateOnly(2026, 8, 23), To: new DateOnly(2026, 8, 23), TemplateId: "utc-order"), CancellationToken.None);

        result.Items.Take(2).Select(item => item.Record.Id).Should().Equal(laterUtc.Id, earlierUtc.Id);
        result.Items.Skip(2).Select(item => item.Record.Id).Should().Equal(new[] { tieOne.Id, tieTwo.Id }.OrderBy(id => id));
    }

    [Fact]
    public async Task Gives_header_project_part_priority_over_a_payload_project_and_survives_corrupt_json()
    {
        var conflict = await SaveAsync("production-daily", new DateOnly(2026, 8, 24), "customer", "header project", "AC-13", "{\"project\":\"payload project\"}", "project priority", RecordStatus.Completed, 27);
        var fallback = await SaveAsync("production-daily", new DateOnly(2026, 8, 25), "broken-payload", "p", "AC-13", "{\"customer\":\"broken-payload\",\"projectPart\":\"payload fallback\"}", "safe malformed header", RecordStatus.Completed, 28, includeHeaderFields: false);
        var corruptPayload = await SaveAsync("production-daily", new DateOnly(2026, 8, 26), "no-header", "p", "AC-13", "{", "safe malformed payload", RecordStatus.Completed, 29, includeHeaderFields: false);
        await UpdateHeaderAsync(fallback.Id, "{");

        var project = await _repository.SearchAsync(new RecordQuery(Project: "header project"), CancellationToken.None);
        var payloadFallback = await _repository.SearchAsync(new RecordQuery(Customer: "broken-payload"), CancellationToken.None);
        var corruptPayloadSearch = await _repository.SearchAsync(new RecordQuery(Keyword: "safe malformed payload"), CancellationToken.None);

        project.Items.Should().ContainSingle().Which.Record.Id.Should().Be(conflict.Id);
        project.Items.Single().Project.Should().Be("header project");
        payloadFallback.Items.Should().ContainSingle().Which.Customer.Should().Be("broken-payload");
        corruptPayloadSearch.Items.Should().ContainSingle().Which.Record.Id.Should().Be(corruptPayload.Id);
        corruptPayloadSearch.Items.Single().Customer.Should().BeNull();
    }

    [Theory]
    [InlineData("100%", "literal 100% value")]
    [InlineData("under_score", "literal under_score value")]
    [InlineData("back\\slash", "literal back\\slash value")]
    public async Task Escapes_like_metacharacters_in_user_filters(string customer, string searchText)
    {
        var record = await SaveAsync("production-daily", new DateOnly(2026, 8, 20), customer, "special", "AC-13", "{}", searchText, RecordStatus.Completed, 20);
        await SaveAsync("production-daily", new DateOnly(2026, 8, 20), customer.Replace("%", "x").Replace("_", "x").Replace("\\", "x"), "special", "AC-13", "{}", searchText, RecordStatus.Completed, 30);

        var result = await _repository.SearchAsync(new RecordQuery(Customer: customer, Keyword: searchText), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Record.Id.Should().Be(record.Id);
    }

    [Fact]
    public async Task Treats_sql_injection_text_as_data()
    {
        var result = await _repository.SearchAsync(new RecordQuery(Customer: "' OR 1=1 --", Keyword: "' OR 1=1 --"), CancellationToken.None);

        result.TotalCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public void Rejects_invalid_page_ranges(int page, int pageSize)
    {
        var create = () => new RecordQuery(Page: page, PageSize: pageSize);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    private async Task<FormRecord> SaveAsync(string template, DateOnly date, string customer, string project, string specification, string payload, string searchText, RecordStatus status, int minute, bool includeHeaderFields = true)
    {
        var headers = new Dictionary<string, string> { ["company"] = "沥青厂" };
        if (includeHeaderFields)
        {
            headers["customer"] = customer;
            headers["projectPart"] = project;
            headers["specification"] = specification;
        }

        var record = FormRecord.CreateDraft(template, 1, date, date, headers, payload, searchText, DateTimeOffset.Parse($"2026-08-01T08:{minute:00}:00+08:00"));
        if (status == RecordStatus.Completed) record.MarkCompleted();
        await _repository.SaveAsync(record, CancellationToken.None);
        return record;
    }

    private async Task UpdateTimestampAsync(Guid id, string updatedAt)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE records SET updated_at = @updatedAt WHERE id = @id;";
        command.Parameters.AddWithValue("@updatedAt", updatedAt);
        command.Parameters.AddWithValue("@id", id.ToString("D"));
        await command.ExecuteNonQueryAsync();
    }

    private async Task UpdateHeaderAsync(Guid id, string headerSnapshotJson)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE records SET header_snapshot_json = @header WHERE id = @id;";
        command.Parameters.AddWithValue("@header", headerSnapshotJson);
        command.Parameters.AddWithValue("@id", id.ToString("D"));
        await command.ExecuteNonQueryAsync();
    }
}
