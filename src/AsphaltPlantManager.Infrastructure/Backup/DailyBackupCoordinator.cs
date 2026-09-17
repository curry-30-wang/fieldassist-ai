using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AsphaltPlantManager.Core.Backup;
using AsphaltPlantManager.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace AsphaltPlantManager.Infrastructure.Backup;

public sealed class DailyBackupCoordinator
{
    internal const string LastSuccessfulDailyBackupDateKey = "last_successful_daily_backup_date";
    private const string DateFormat = "yyyy-MM-dd";
    private readonly IBackupService _backupService;
    private readonly string _connectionString;
    private readonly string _destinationRoot;
    private readonly TimeProvider _timeProvider;
    private readonly string _mutexName;

    public DailyBackupCoordinator(
        IBackupService backupService,
        string connectionString,
        string destinationRoot,
        TimeProvider? timeProvider = null)
    {
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        _connectionString = connectionString;
        _destinationRoot = Path.GetFullPath(destinationRoot);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _mutexName = CreateMutexName(connectionString);
    }

    public Task<bool> TryCreateOnNormalExitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            using var mutex = new Mutex(initiallyOwned: false, _mutexName);
            var ownsMutex = false;
            try
            {
                try
                {
                    var signaled = WaitHandle.WaitAny([mutex, cancellationToken.WaitHandle]);
                    if (signaled != 0)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                }
                catch (AbandonedMutexException)
                {
                    // The abandoned owner no longer runs; this thread now owns the mutex.
                }

                ownsMutex = true;
                return TryCreateUnderProcessLockAsync(cancellationToken).GetAwaiter().GetResult();
            }
            finally
            {
                if (ownsMutex)
                {
                    mutex.ReleaseMutex();
                }
            }
        }, CancellationToken.None);
    }

    private async Task<bool> TryCreateUnderProcessLockAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var lastSuccessfulDate = await ReadLastSuccessfulDateAsync(cancellationToken).ConfigureAwait(false);
        if (lastSuccessfulDate == today)
        {
            return false;
        }

        _ = await _backupService.CreateAsync(
            BackupReason.AutomaticDaily,
            _destinationRoot,
            cancellationToken).ConfigureAwait(false);
        await _backupService.PruneAsync(_destinationRoot, 30, cancellationToken).ConfigureAwait(false);
        await WriteLastSuccessfulDateAsync(today, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<DateOnly?> ReadLastSuccessfulDateAsync(CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await DatabaseInitializer.ApplyConnectionPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE setting_key = $key;";
        command.Parameters.AddWithValue("$key", LastSuccessfulDailyBackupDateKey);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private async Task WriteLastSuccessfulDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await using var access = await SqliteDatabaseAccess.AcquireReadForConnectionStringAsync(_connectionString, cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await DatabaseInitializer.ApplyConnectionPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO settings (setting_key, value, updated_at) VALUES ($key, $value, $updatedAt) " +
            "ON CONFLICT(setting_key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;";
        command.Parameters.AddWithValue("$key", LastSuccessfulDailyBackupDateKey);
        command.Parameters.AddWithValue("$value", date.ToString(DateFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAt", _timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string CreateMutexName(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var canonicalDataSource = Path.GetFullPath(dataSource).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalDataSource));
        return $@"Local\AsphaltPlantManager-DailyBackup-{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }
}
