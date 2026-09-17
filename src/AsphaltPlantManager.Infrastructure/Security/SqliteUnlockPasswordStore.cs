using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Infrastructure.Database;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;

namespace AsphaltPlantManager.Infrastructure.Security;

public sealed class SqliteUnlockPasswordStore : IUnlockPasswordStore
{
    private const string PasswordKey = "archive_unlock_password";
    private const int Iterations = 100_000;
    private readonly string _connectionString;
    public SqliteUnlockPasswordStore(string connectionString) => _connectionString = connectionString;

    public async Task<bool> HasPasswordAsync(CancellationToken cancellationToken) => (await ReadAsync(cancellationToken).ConfigureAwait(false)) is not null;

    public async Task SetPasswordAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var value = JsonSerializer.Serialize(new StoredPassword(Convert.ToBase64String(salt), Convert.ToBase64String(hash), Iterations));
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO settings (setting_key, value, updated_at) VALUES (@Key, @Value, @UpdatedAt) ON CONFLICT(setting_key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;", new { Key = PasswordKey, Value = value, UpdatedAt = DateTimeOffset.UtcNow.ToString("O") }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<bool> VerifyAsync(string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;
        try
        {
            var stored = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (stored is null || stored.Iterations < Iterations || string.IsNullOrWhiteSpace(stored.Salt) || string.IsNullOrWhiteSpace(stored.Hash))
            {
                return false;
            }

            var salt = Convert.FromBase64String(stored.Salt);
            var expectedHash = Convert.FromBase64String(stored.Hash);
            if (salt.Length != 16 || expectedHash.Length != 32)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, stored.Iterations, HashAlgorithmName.SHA256, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expectedHash);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or CryptographicException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private async Task<StoredPassword?> ReadAsync(CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var value = await connection.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT value FROM settings WHERE setting_key = @Key;", new { Key = PasswordKey }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return value is null ? null : JsonSerializer.Deserialize<StoredPassword>(value);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try { await connection.OpenAsync(cancellationToken).ConfigureAwait(false); await DatabaseInitializer.ApplyConnectionPragmasAsync(connection, cancellationToken).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private sealed record StoredPassword(string? Salt, string? Hash, int Iterations);
}
