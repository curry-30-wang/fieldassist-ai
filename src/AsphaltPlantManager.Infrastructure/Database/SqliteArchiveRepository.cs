using AsphaltPlantManager.Core.Records;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace AsphaltPlantManager.Infrastructure.Database;

public sealed class SqliteArchiveRepository : IArchiveRepository
{
    private const string DateTimeOffsetFormat = "O";
    private readonly string _connectionString;

    public SqliteArchiveRepository(string connectionString) => _connectionString = connectionString;

    public Task<FormRecord?> GetAsync(Guid recordId, CancellationToken cancellationToken) =>
        new SqliteRecordRepository(_connectionString).GetAsync(recordId, cancellationToken);

    public async Task<ArchiveVersion> SealAsync(Guid recordId, string archivePrefix, string changeNote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeNote);
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var row = await GetRecordRowAsync(connection, transaction, recordId, cancellationToken).ConfigureAwait(false);
        EnsureRecordState(row, RecordStatus.Completed, "仅已完成表单可首次封存。");
        var key = $"{archivePrefix.Trim().ToUpperInvariant()}-{now:yyyyMM}";
        var nextValue = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO archive_sequences (sequence_key, next_value) VALUES (@Key, 2) ON CONFLICT(sequence_key) DO UPDATE SET next_value = next_value + 1 RETURNING next_value - 1;",
            new { Key = key }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var archiveNumber = $"{key}-{nextValue:0000}";
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE records SET status = @Status, archive_number = @ArchiveNumber, change_note = @ChangeNote, sealed_at = @SealedAt, updated_at = @UpdatedAt WHERE id = @Id AND status = @Completed;",
            new { Id = recordId.ToString("D"), Status = (int)RecordStatus.Sealed, ArchiveNumber = archiveNumber, ChangeNote = changeNote, SealedAt = Format(now), UpdatedAt = Format(now), Completed = (int)RecordStatus.Completed }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var version = CreateVersion(row, 1, archiveNumber, changeNote, now);
        await InsertVersionAsync(connection, transaction, version, cancellationToken).ConfigureAwait(false);
        await PruneOldVersionsAsync(connection, transaction, recordId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return version;
    }

    public async Task<ArchiveVersion> SaveArchivedEditAsync(Guid recordId, string payloadJson, IReadOnlyDictionary<string, string> headerSnapshot, string changeNote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentNullException.ThrowIfNull(headerSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeNote);
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var row = await GetRecordRowAsync(connection, transaction, recordId, cancellationToken).ConfigureAwait(false);
        EnsureRecordState(row, RecordStatus.Sealed, "仅已封存表单可保存归档修改。");
        var headerJson = JsonSerializer.Serialize(headerSnapshot);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE records SET header_snapshot_json = @HeaderSnapshotJson, payload_json = @PayloadJson, updated_at = @UpdatedAt WHERE id = @Id;",
            new { Id = recordId.ToString("D"), HeaderSnapshotJson = headerJson, PayloadJson = payloadJson, UpdatedAt = Format(now) }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        row.HeaderSnapshotJson = headerJson;
        row.PayloadJson = payloadJson;
        var version = CreateVersion(row, await NextVersionAsync(connection, transaction, recordId, cancellationToken).ConfigureAwait(false), row.ArchiveNumber!, changeNote, now);
        await InsertVersionAsync(connection, transaction, version, cancellationToken).ConfigureAwait(false);
        await PruneOldVersionsAsync(connection, transaction, recordId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return version;
    }

    public async Task<ArchiveVersion> RestoreVersionAsync(Guid recordId, int version, string changeNote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeNote);
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var record = await GetRecordRowAsync(connection, transaction, recordId, cancellationToken).ConfigureAwait(false);
        EnsureRecordState(record, RecordStatus.Sealed, "仅已封存表单可恢复历史版本。");
        var restored = await connection.QuerySingleOrDefaultAsync<VersionRow>(new CommandDefinition(
            "SELECT record_id AS RecordId, version AS Version, archive_number AS ArchiveNumber, template_id AS TemplateId, template_version AS TemplateVersion, header_snapshot_json AS HeaderSnapshotJson, payload_json AS PayloadJson, change_note AS ChangeNote, created_at AS CreatedAt FROM record_versions WHERE record_id = @RecordId AND version = @Version;",
            new { RecordId = recordId.ToString("D"), Version = version }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("未找到要恢复的历史版本。");
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE records SET header_snapshot_json = @HeaderSnapshotJson, payload_json = @PayloadJson, updated_at = @UpdatedAt WHERE id = @Id;",
            new { Id = recordId.ToString("D"), restored.HeaderSnapshotJson, restored.PayloadJson, UpdatedAt = Format(now) }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        record.HeaderSnapshotJson = restored.HeaderSnapshotJson;
        record.PayloadJson = restored.PayloadJson;
        var result = CreateVersion(record, await NextVersionAsync(connection, transaction, recordId, cancellationToken).ConfigureAwait(false), record.ArchiveNumber!, changeNote, now);
        await InsertVersionAsync(connection, transaction, result, cancellationToken).ConfigureAwait(false);
        await PruneOldVersionsAsync(connection, transaction, recordId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<ArchiveVersion>> GetVersionsAsync(Guid recordId, CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<VersionRow>(new CommandDefinition(
            "SELECT record_id AS RecordId, version AS Version, archive_number AS ArchiveNumber, template_id AS TemplateId, template_version AS TemplateVersion, header_snapshot_json AS HeaderSnapshotJson, payload_json AS PayloadJson, change_note AS ChangeNote, created_at AS CreatedAt FROM record_versions WHERE record_id = @RecordId ORDER BY version;",
            new { RecordId = recordId.ToString("D") }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(ToVersion).ToArray();
    }

    public async Task<bool> MoveToTrashAsync(Guid recordId, DateTimeOffset deletedAt, CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var changed = await connection.ExecuteAsync(new CommandDefinition("UPDATE records SET is_deleted = 1 WHERE id = @Id AND is_deleted = 0;", new { Id = recordId.ToString("D") }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (changed == 1)
        {
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO trash (record_id, deleted_at) VALUES (@Id, @DeletedAt);", new { Id = recordId.ToString("D"), DeletedAt = Format(deletedAt) }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return changed == 1;
    }

    public async Task<bool> RestoreFromTrashAsync(Guid recordId, CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var removed = await connection.ExecuteAsync(new CommandDefinition("DELETE FROM trash WHERE record_id = @Id;", new { Id = recordId.ToString("D") }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (removed == 1)
        {
            await connection.ExecuteAsync(new CommandDefinition("UPDATE records SET is_deleted = 0 WHERE id = @Id;", new { Id = recordId.ToString("D") }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return removed == 1;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try { await connection.OpenAsync(cancellationToken).ConfigureAwait(false); await DatabaseInitializer.ApplyConnectionPragmasAsync(connection, cancellationToken).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static async Task<RecordRow> GetRecordRowAsync(SqliteConnection connection, DbTransaction transaction, Guid id, CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition("SELECT id AS Id, template_id AS TemplateId, template_version AS TemplateVersion, status AS Status, archive_number AS ArchiveNumber, header_snapshot_json AS HeaderSnapshotJson, payload_json AS PayloadJson FROM records WHERE id = @Id;", new { Id = id.ToString("D") }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
        ?? throw new KeyNotFoundException("未找到表单。");

    private static async Task<int> NextVersionAsync(SqliteConnection connection, DbTransaction transaction, Guid recordId, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COALESCE(MAX(version), 0) + 1 FROM record_versions WHERE record_id = @RecordId;", new { RecordId = recordId.ToString("D") }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

    private static ArchiveVersion CreateVersion(RecordRow row, int version, string archiveNumber, string changeNote, DateTimeOffset now) =>
        new(Guid.Parse(row.Id), version, archiveNumber, row.TemplateId, row.TemplateVersion, JsonSerializer.Deserialize<Dictionary<string, string>>(row.HeaderSnapshotJson) ?? [], row.PayloadJson, changeNote, now);

    private static ArchiveVersion ToVersion(VersionRow row) => new(Guid.Parse(row.RecordId), row.Version, row.ArchiveNumber, row.TemplateId, row.TemplateVersion, JsonSerializer.Deserialize<Dictionary<string, string>>(row.HeaderSnapshotJson) ?? [], row.PayloadJson, row.ChangeNote, DateTimeOffset.ParseExact(row.CreatedAt, DateTimeOffsetFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));

    private static Task InsertVersionAsync(SqliteConnection connection, DbTransaction transaction, ArchiveVersion version, CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
        "INSERT INTO record_versions (record_id, version, archive_number, template_id, template_version, header_snapshot_json, payload_json, change_note, created_at) VALUES (@RecordId, @Version, @ArchiveNumber, @TemplateId, @TemplateVersion, @HeaderSnapshotJson, @PayloadJson, @ChangeNote, @CreatedAt);",
        new { RecordId = version.RecordId.ToString("D"), version.Version, version.ArchiveNumber, version.TemplateId, version.TemplateVersion, HeaderSnapshotJson = JsonSerializer.Serialize(version.HeaderSnapshot), version.PayloadJson, version.ChangeNote, CreatedAt = Format(version.CreatedAt) }, transaction, cancellationToken: cancellationToken));

    private static Task PruneOldVersionsAsync(SqliteConnection connection, DbTransaction transaction, Guid recordId, CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
        "DELETE FROM record_versions WHERE record_id = @RecordId AND version < (SELECT MAX(version) - 1 FROM record_versions WHERE record_id = @RecordId);",
        new { RecordId = recordId.ToString("D") }, transaction, cancellationToken: cancellationToken));

    private static void EnsureRecordState(RecordRow row, RecordStatus expected, string message)
    {
        if (row.Status != (int)expected) throw new InvalidOperationException(message);
    }
    private static string Format(DateTimeOffset value) => value.ToString(DateTimeOffsetFormat, CultureInfo.InvariantCulture);
    private sealed class RecordRow { public string Id { get; init; } = null!; public string TemplateId { get; init; } = null!; public int TemplateVersion { get; init; } public int Status { get; init; } public string? ArchiveNumber { get; init; } public string HeaderSnapshotJson { get; set; } = null!; public string PayloadJson { get; set; } = null!; }
    private sealed class VersionRow { public string RecordId { get; init; } = null!; public int Version { get; init; } public string ArchiveNumber { get; init; } = null!; public string TemplateId { get; init; } = null!; public int TemplateVersion { get; init; } public string HeaderSnapshotJson { get; init; } = null!; public string PayloadJson { get; init; } = null!; public string ChangeNote { get; init; } = null!; public string CreatedAt { get; init; } = null!; }
}
