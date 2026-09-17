using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Search;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace AsphaltPlantManager.Infrastructure.Database;

public sealed class SqliteRecordRepository : IRecordRepository
{
    private const string DateOnlyFormat = "yyyy-MM-dd";
    private const string DateTimeOffsetFormat = "O";
    private readonly string _connectionString;

    public SqliteRecordRepository(string connectionString) => _connectionString = connectionString;

    public async Task SaveAsync(FormRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO records (id, template_id, template_version, period_start, period_end, status, archive_number, change_note, sealed_at, header_snapshot_json, payload_json, search_text, created_at, updated_at, is_deleted)
            VALUES (@Id, @TemplateId, @TemplateVersion, @PeriodStart, @PeriodEnd, @Status, @ArchiveNumber, @ChangeNote, @SealedAt, @HeaderSnapshotJson, @PayloadJson, @SearchText, @CreatedAt, @UpdatedAt, @IsDeleted)
            ON CONFLICT(id) DO UPDATE SET
                template_id = excluded.template_id,
                template_version = excluded.template_version,
                period_start = excluded.period_start,
                period_end = excluded.period_end,
                status = excluded.status,
                archive_number = excluded.archive_number,
                change_note = excluded.change_note,
                sealed_at = excluded.sealed_at,
                header_snapshot_json = excluded.header_snapshot_json,
                payload_json = excluded.payload_json,
                search_text = excluded.search_text,
                updated_at = excluded.updated_at,
                is_deleted = excluded.is_deleted;
            """,
            ToParameters(record),
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FormRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition(
            "SELECT id AS Id, template_id AS TemplateId, template_version AS TemplateVersion, period_start AS PeriodStart, period_end AS PeriodEnd, status AS Status, archive_number AS ArchiveNumber, change_note AS ChangeNote, sealed_at AS SealedAt, header_snapshot_json AS HeaderSnapshotJson, payload_json AS PayloadJson, search_text AS SearchText, created_at AS CreatedAt, updated_at AS UpdatedAt, is_deleted AS IsDeleted FROM records WHERE id = @Id;",
            new { Id = id.ToString("D") },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : ToRecord(row);
    }

    public async Task<PagedResult> SearchAsync(RecordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var (whereClause, parameters) = BuildSearchWhereClause(query);
        var totalCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM records WHERE {whereClause};", parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        parameters.Add("PageSize", query.PageSize);
        parameters.Add("Offset", checked(((long)query.Page - 1) * query.PageSize));
        var rows = await connection.QueryAsync<RecordRow>(new CommandDefinition(
            $"SELECT id AS Id, template_id AS TemplateId, template_version AS TemplateVersion, period_start AS PeriodStart, period_end AS PeriodEnd, status AS Status, archive_number AS ArchiveNumber, change_note AS ChangeNote, sealed_at AS SealedAt, header_snapshot_json AS HeaderSnapshotJson, payload_json AS PayloadJson, search_text AS SearchText, created_at AS CreatedAt, updated_at AS UpdatedAt, is_deleted AS IsDeleted FROM records WHERE {whereClause} ORDER BY period_start DESC, julianday(updated_at) DESC, id ASC LIMIT @PageSize OFFSET @Offset;",
            parameters,
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult(rows.Select(row => new SearchResult(ToRecord(row))).ToArray(), totalCount, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<FormRecord>> GetActiveRecordsSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<RecordRow>(new CommandDefinition(
            "SELECT id AS Id, template_id AS TemplateId, template_version AS TemplateVersion, period_start AS PeriodStart, period_end AS PeriodEnd, status AS Status, archive_number AS ArchiveNumber, change_note AS ChangeNote, sealed_at AS SealedAt, header_snapshot_json AS HeaderSnapshotJson, payload_json AS PayloadJson, search_text AS SearchText, created_at AS CreatedAt, updated_at AS UpdatedAt, is_deleted AS IsDeleted FROM records WHERE is_deleted = 0 ORDER BY period_start DESC, julianday(updated_at) DESC, id ASC;",
            transaction: transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToRecord).ToArray();
    }

    private static (string WhereClause, DynamicParameters Parameters) BuildSearchWhereClause(RecordQuery query)
    {
        const string Customer = "COALESCE(NULLIF(CASE WHEN json_valid(header_snapshot_json) THEN json_extract(header_snapshot_json, '$.customer') END, ''), CASE WHEN json_valid(payload_json) THEN json_extract(payload_json, '$.customer') END)";
        const string Project = "COALESCE(NULLIF(CASE WHEN json_valid(header_snapshot_json) THEN json_extract(header_snapshot_json, '$.project') END, ''), NULLIF(CASE WHEN json_valid(header_snapshot_json) THEN json_extract(header_snapshot_json, '$.projectPart') END, ''), CASE WHEN json_valid(payload_json) THEN json_extract(payload_json, '$.project') END, CASE WHEN json_valid(payload_json) THEN json_extract(payload_json, '$.projectPart') END)";
        const string Specification = "COALESCE(NULLIF(CASE WHEN json_valid(header_snapshot_json) THEN json_extract(header_snapshot_json, '$.specification') END, ''), CASE WHEN json_valid(payload_json) THEN json_extract(payload_json, '$.specification') END)";
        const string Receivable = "CASE WHEN json_valid(payload_json) AND json_type(payload_json, '$.receivable') IN ('integer', 'real') THEN CAST(json_extract(payload_json, '$.receivable') AS REAL) END";

        var clauses = new List<string>();
        var parameters = new DynamicParameters();
        if (!query.IncludeDeleted)
        {
            clauses.Add("is_deleted = 0");
        }

        if (query.From is not null)
        {
            clauses.Add("period_start >= @From");
            parameters.Add("From", query.From.Value.ToString(DateOnlyFormat, CultureInfo.InvariantCulture));
        }

        if (query.To is not null)
        {
            clauses.Add("period_start <= @To");
            parameters.Add("To", query.To.Value.ToString(DateOnlyFormat, CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(query.TemplateId))
        {
            clauses.Add("template_id = @TemplateId");
            parameters.Add("TemplateId", query.TemplateId);
        }

        if (query.Status is not null)
        {
            clauses.Add("status = @Status");
            parameters.Add("Status", (int)query.Status.Value);
        }

        AddLikeClause(query.Customer, Customer, "Customer", clauses, parameters);
        AddLikeClause(query.Project, Project, "Project", clauses, parameters);
        AddLikeClause(query.Specification, Specification, "Specification", clauses, parameters);

        if (query.HasReceivable is not null)
        {
            clauses.Add(query.HasReceivable.Value ? $"{Receivable} > 0" : $"COALESCE({Receivable}, 0) <= 0");
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            clauses.Add("(archive_number LIKE @Keyword ESCAPE '\\' OR search_text LIKE @Keyword ESCAPE '\\' OR header_snapshot_json LIKE @Keyword ESCAPE '\\' OR payload_json LIKE @Keyword ESCAPE '\\')");
            parameters.Add("Keyword", ToContainsPattern(query.Keyword));
        }

        return (clauses.Count == 0 ? "1 = 1" : string.Join(" AND ", clauses), parameters);
    }

    private static void AddLikeClause(string? value, string expression, string parameterName, ICollection<string> clauses, DynamicParameters parameters)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            clauses.Add($"{expression} LIKE @{parameterName} ESCAPE '\\'");
            parameters.Add(parameterName, ToContainsPattern(value));
        }
    }

    private static string ToContainsPattern(string value) => $"%{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await DatabaseInitializer.ApplyConnectionPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static object ToParameters(FormRecord record) => new
    {
        Id = record.Id.ToString("D"),
        record.TemplateId,
        record.TemplateVersion,
        PeriodStart = record.PeriodStart.ToString(DateOnlyFormat, CultureInfo.InvariantCulture),
        PeriodEnd = record.PeriodEnd.ToString(DateOnlyFormat, CultureInfo.InvariantCulture),
        Status = (int)record.Status,
        record.ArchiveNumber,
        record.ChangeNote,
        SealedAt = record.SealedAt?.ToUniversalTime().ToString(DateTimeOffsetFormat, CultureInfo.InvariantCulture),
        HeaderSnapshotJson = JsonSerializer.Serialize(record.HeaderSnapshot),
        record.PayloadJson,
        record.SearchText,
        CreatedAt = record.CreatedAt.ToUniversalTime().ToString(DateTimeOffsetFormat, CultureInfo.InvariantCulture),
        UpdatedAt = record.UpdatedAt.ToUniversalTime().ToString(DateTimeOffsetFormat, CultureInfo.InvariantCulture),
        IsDeleted = record.IsDeleted ? 1 : 0
    };

    private static FormRecord ToRecord(RecordRow row) => FormRecord.Restore(
        Guid.Parse(row.Id),
        row.TemplateId,
        row.TemplateVersion,
        DateOnly.ParseExact(row.PeriodStart, DateOnlyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None),
        DateOnly.ParseExact(row.PeriodEnd, DateOnlyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None),
        (RecordStatus)row.Status,
        row.ArchiveNumber,
        row.ChangeNote,
        row.SealedAt is null ? null : DateTimeOffset.ParseExact(row.SealedAt, DateTimeOffsetFormat, CultureInfo.InvariantCulture, DateTimeStyles.None),
        TryDeserializeHeaderSnapshot(row.HeaderSnapshotJson),
        row.PayloadJson,
        row.SearchText,
        DateTimeOffset.ParseExact(row.CreatedAt, DateTimeOffsetFormat, CultureInfo.InvariantCulture, DateTimeStyles.None),
        DateTimeOffset.ParseExact(row.UpdatedAt, DateTimeOffsetFormat, CultureInfo.InvariantCulture, DateTimeStyles.None),
        row.IsDeleted != 0);

    private static IReadOnlyDictionary<string, string> TryDeserializeHeaderSnapshot(string headerSnapshotJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(headerSnapshotJson) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private sealed class RecordRow
    {
        public string Id { get; init; } = null!;
        public string TemplateId { get; init; } = null!;
        public int TemplateVersion { get; init; }
        public string PeriodStart { get; init; } = null!;
        public string PeriodEnd { get; init; } = null!;
        public int Status { get; init; }
        public string? ArchiveNumber { get; init; }
        public string? ChangeNote { get; init; }
        public string? SealedAt { get; init; }
        public string HeaderSnapshotJson { get; init; } = null!;
        public string PayloadJson { get; init; } = null!;
        public string SearchText { get; init; } = null!;
        public string CreatedAt { get; init; } = null!;
        public string UpdatedAt { get; init; } = null!;
        public int IsDeleted { get; init; }
    }
}
