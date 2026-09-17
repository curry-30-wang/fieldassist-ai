using Dapper;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Reflection;

namespace AsphaltPlantManager.Infrastructure.Database;

public sealed class DatabaseInitializer
{
    private static readonly (string Version, string ResourceName)[] Migrations =
    [
        ("001_initial", "AsphaltPlantManager.Infrastructure.Database.Migrations.001_initial.sql"),
        ("002_customer_receivables", "AsphaltPlantManager.Infrastructure.Database.Migrations.002_customer_receivables.sql")
    ];
    private readonly string _connectionString;

    public DatabaseInitializer(string connectionString) => _connectionString = connectionString;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition("PRAGMA journal_mode = WAL;", cancellationToken: cancellationToken)).ConfigureAwait(false);
        await ApplyConnectionPragmasAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "CREATE TABLE IF NOT EXISTS schema_migrations (version TEXT NOT NULL PRIMARY KEY, applied_at TEXT NOT NULL);",
            transaction: transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        foreach (var migration in Migrations)
        {
            var applied = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM schema_migrations WHERE version = @Version;",
                new { migration.Version }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (applied == 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(ReadMigration(migration.ResourceName), transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                await connection.ExecuteAsync(new CommandDefinition("INSERT INTO schema_migrations (version, applied_at) VALUES (@Version, @AppliedAt);", new { migration.Version, AppliedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
        }

        await EnsureRecordVersionColumnsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task ApplyConnectionPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;", cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static async Task EnsureRecordVersionColumnsAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        var columns = (await connection.QueryAsync<string>(new CommandDefinition("SELECT name FROM pragma_table_info('record_versions');", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, type) in new[] { ("archive_number", "TEXT"), ("template_id", "TEXT"), ("template_version", "INTEGER"), ("header_snapshot_json", "TEXT") })
        {
            if (!columns.Contains(name))
            {
                await connection.ExecuteAsync(new CommandDefinition($"ALTER TABLE record_versions ADD COLUMN {name} {type} NULL;", transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
        }
    }

    private static string ReadMigration(string resourceName)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The database migration '{resourceName}' could not be found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
