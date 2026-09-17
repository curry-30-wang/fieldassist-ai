namespace AsphaltPlantManager.Core.Records;

public sealed class DraftAutoSaveService : IAsyncDisposable
{
    private readonly IRecordRepository _repository;
    private readonly TimeSpan _debounceDelay;
    private readonly object _gate = new();
    private CancellationTokenSource? _debounceCancellation;
    private FormRecord? _pendingRecord;
    private Task _saveTail = Task.CompletedTask;
    private bool _disposed;

    public DraftAutoSaveService(IRecordRepository repository, TimeSpan? debounceDelay = null)
    {
        _repository = repository;
        _debounceDelay = debounceDelay ?? TimeSpan.FromSeconds(1);
        if (_debounceDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceDelay));
        }
    }

    public void NotifyChanged(FormRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _debounceCancellation?.Cancel();
            _pendingRecord = record;
            var cancellation = new CancellationTokenSource();
            _debounceCancellation = cancellation;
            _ = SaveAfterDelayAsync(cancellation);
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken) => FlushCore(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        Task saveTail;
        lock (_gate)
        {
            if (_disposed)
            {
                saveTail = _saveTail;
            }
            else
            {
                _disposed = true;
                saveTail = FlushCoreLocked(CancellationToken.None);
            }
        }

        await saveTail.ConfigureAwait(false);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource debounceCancellation)
    {
        try
        {
            await Task.Delay(_debounceDelay, debounceCancellation.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (!ReferenceEquals(_debounceCancellation, debounceCancellation))
                {
                    return;
                }

                _debounceCancellation = null;
                if (_pendingRecord is not null)
                {
                    EnqueueSaveLocked(_pendingRecord, CancellationToken.None);
                    _pendingRecord = null;
                }
            }
        }
        catch (OperationCanceledException) when (debounceCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            debounceCancellation.Dispose();
        }
    }

    private Task FlushCore(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return FlushCoreLocked(cancellationToken);
        }
    }

    private Task FlushCoreLocked(CancellationToken cancellationToken)
    {
        _debounceCancellation?.Cancel();
        _debounceCancellation = null;
        if (_pendingRecord is not null)
        {
            EnqueueSaveLocked(_pendingRecord, cancellationToken);
            _pendingRecord = null;
        }

        return _saveTail;
    }

    private void EnqueueSaveLocked(FormRecord record, CancellationToken cancellationToken)
    {
        var previousSave = _saveTail;
        _saveTail = SaveAfterPreviousAsync(previousSave, record, cancellationToken);
        _ = _saveTail.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task SaveAfterPreviousAsync(Task previousSave, FormRecord record, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            await previousSave.ConfigureAwait(false);
        }
        catch
        {
            // A previous Flush caller receives its own error; later drafts must still be persisted.
        }

        await _repository.SaveAsync(record, cancellationToken).ConfigureAwait(false);
    }
}
