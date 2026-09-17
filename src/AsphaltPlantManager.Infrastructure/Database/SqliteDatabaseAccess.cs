using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace AsphaltPlantManager.Infrastructure.Database;

internal static class SqliteDatabaseAccess
{
    private static readonly ConcurrentDictionary<string, DatabaseGate> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal static ValueTask<IAsyncDisposable> AcquireReadForConnectionStringAsync(
        string connectionString,
        CancellationToken cancellationToken) =>
        AcquireReadForPathAsync(new SqliteConnectionStringBuilder(connectionString).DataSource, cancellationToken);

    internal static ValueTask<IAsyncDisposable> AcquireReadForPathAsync(
        string databasePath,
        CancellationToken cancellationToken) =>
        GetGate(databasePath).AcquireReadAsync(cancellationToken);

    internal static ValueTask<IAsyncDisposable> AcquireExclusiveForPathAsync(
        string databasePath,
        CancellationToken cancellationToken) =>
        GetGate(databasePath).AcquireWriteAsync(cancellationToken);

    private static DatabaseGate GetGate(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return Gates.GetOrAdd(Path.GetFullPath(databasePath), static _ => new DatabaseGate());
    }

    private sealed class DatabaseGate
    {
        private readonly SemaphoreSlim _readerTurnstile = new(1, 1);
        private readonly SemaphoreSlim _resource = new(1, 1);
        private readonly SemaphoreSlim _readerMutex = new(1, 1);
        private int _readerCount;

        internal async ValueTask<IAsyncDisposable> AcquireReadAsync(CancellationToken cancellationToken)
        {
            await _readerTurnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
            _readerTurnstile.Release();

            await _readerMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_readerCount == 0)
                {
                    await _resource.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                _readerCount++;
            }
            finally
            {
                _readerMutex.Release();
            }

            return new Lease(ReleaseRead);
        }

        internal async ValueTask<IAsyncDisposable> AcquireWriteAsync(CancellationToken cancellationToken)
        {
            await _readerTurnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _resource.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _readerTurnstile.Release();
                throw;
            }

            return new Lease(ReleaseWrite);
        }

        private void ReleaseRead()
        {
            _readerMutex.Wait();
            try
            {
                _readerCount--;
                if (_readerCount == 0)
                {
                    _resource.Release();
                }
            }
            finally
            {
                _readerMutex.Release();
            }
        }

        private void ReleaseWrite()
        {
            _resource.Release();
            _readerTurnstile.Release();
        }
    }

    private sealed class Lease(Action release) : IAsyncDisposable
    {
        private Action? _release = release;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
