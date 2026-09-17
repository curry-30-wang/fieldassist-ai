using AsphaltPlantManager.Core.Dashboard;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AsphaltPlantManager.Infrastructure.Database;

public sealed class SqliteSettingsReader : ISettingsReader
{
    private readonly string _connectionString;

    public SqliteSettingsReader(string connectionString) => _connectionString = connectionString;

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await DatabaseInitializer.ApplyConnectionPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value FROM settings WHERE setting_key = @Key;", new { Key = key }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
