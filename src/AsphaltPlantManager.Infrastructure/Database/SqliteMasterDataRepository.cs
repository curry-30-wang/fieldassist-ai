using AsphaltPlantManager.Core.MasterData;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace AsphaltPlantManager.Infrastructure.Database;

public sealed class SqliteMasterDataRepository : IMasterDataRepository
{
    private readonly string _connectionString;

    public SqliteMasterDataRepository(string connectionString) => _connectionString = connectionString;

    public async Task UpsertAsync(MasterDataItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO master_data (category, item_key, value, updated_at) VALUES (@Category, @Key, @Value, @UpdatedAt) ON CONFLICT(category, item_key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;",
            new { item.Category, item.Key, item.Value, UpdatedAt = (item.UpdatedAt ?? DateTimeOffset.UtcNow).ToString("O", CultureInfo.InvariantCulture) },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MasterDataItem?> GetAsync(string category, string key, CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<MasterDataRow>(new CommandDefinition(
            "SELECT category AS Category, item_key AS ItemKey, value AS Value, updated_at AS UpdatedAt FROM master_data WHERE category = @Category AND item_key = @Key;",
            new { Category = category, Key = key },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : new MasterDataItem(row.Category, row.ItemKey, row.Value, DateTimeOffset.ParseExact(row.UpdatedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None));
    }

    public async Task<IReadOnlyList<MasterDataItem>> ListAsync(string category, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<MasterDataRow>(new CommandDefinition(
            "SELECT category AS Category, item_key AS ItemKey, value AS Value, updated_at AS UpdatedAt FROM master_data WHERE category = @Category ORDER BY item_key COLLATE NOCASE;",
            new { Category = category.Trim() }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(ToItem).ToArray();
    }

    public async Task<bool> DeleteAsync(string category, string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM master_data WHERE category = @Category AND item_key = @Key;",
            new { Category = category.Trim(), Key = key.Trim() }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return deleted == 1;
    }

    private static MasterDataItem ToItem(MasterDataRow row) => new(row.Category, row.ItemKey, row.Value, DateTimeOffset.ParseExact(row.UpdatedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None));

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

    private sealed class MasterDataRow
    {
        public string Category { get; init; } = null!;
        public string ItemKey { get; init; } = null!;
        public string Value { get; init; } = null!;
        public string UpdatedAt { get; init; } = null!;
    }
}
