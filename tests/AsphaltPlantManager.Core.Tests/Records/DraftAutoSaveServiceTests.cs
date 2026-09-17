using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Search;
using FluentAssertions;
using Xunit;

namespace AsphaltPlantManager.Core.Tests.Records;

public sealed class DraftAutoSaveServiceTests
{
    [Fact]
    public async Task Consecutive_notifications_save_only_the_latest_record()
    {
        var repository = new RecordingRepository();
        await using var service = new DraftAutoSaveService(repository, TimeSpan.FromMilliseconds(50));
        var first = CreateRecord("first");
        var latest = CreateRecord("latest");

        service.NotifyChanged(first);
        service.NotifyChanged(latest);
        await Task.Delay(150);

        repository.Saved.Should().ContainSingle().Which.Id.Should().Be(latest.Id);
    }

    [Fact]
    public async Task Flush_saves_the_pending_record_once_without_waiting_for_the_debounce_period()
    {
        var repository = new RecordingRepository();
        await using var service = new DraftAutoSaveService(repository, TimeSpan.FromSeconds(10));
        var record = CreateRecord("flush");

        service.NotifyChanged(record);
        await service.FlushAsync(CancellationToken.None);

        repository.Saved.Should().ContainSingle().Which.Id.Should().Be(record.Id);
    }

    [Fact]
    public async Task Disposing_with_pending_save_completes_save_without_unobserved_exception()
    {
        var repository = new RecordingRepository();
        var service = new DraftAutoSaveService(repository, TimeSpan.FromSeconds(10));
        var record = CreateRecord("dispose");

        service.NotifyChanged(record);
        await service.DisposeAsync();

        repository.Saved.Should().ContainSingle().Which.Id.Should().Be(record.Id);
    }

    [Fact]
    public async Task Concurrent_flushes_and_dispose_wait_for_a_save_already_started_by_flush()
    {
        var repository = new BlockingRepository();
        var service = new DraftAutoSaveService(repository, TimeSpan.FromSeconds(10));
        service.NotifyChanged(CreateRecord("in-flight"));

        var firstFlush = service.FlushAsync(CancellationToken.None);
        await repository.SaveStarted.Task;
        var secondFlush = service.FlushAsync(CancellationToken.None);
        var dispose = service.DisposeAsync().AsTask();

        try
        {
            secondFlush.IsCompleted.Should().BeFalse();
            dispose.IsCompleted.Should().BeFalse();
        }
        finally
        {
            repository.ReleaseSave();
            await Task.WhenAll(firstFlush, secondFlush, dispose);
        }

        repository.Saved.Should().ContainSingle();
    }

    [Fact]
    public async Task Notification_received_during_a_flush_save_is_saved_by_a_later_flush()
    {
        var repository = new BlockingRepository();
        await using var service = new DraftAutoSaveService(repository, TimeSpan.FromSeconds(10));
        var first = CreateRecord("first");
        var latest = CreateRecord("latest");
        service.NotifyChanged(first);

        var firstFlush = service.FlushAsync(CancellationToken.None);
        await repository.SaveStarted.Task;
        service.NotifyChanged(latest);
        repository.ReleaseSave();
        await firstFlush;
        await service.FlushAsync(CancellationToken.None);

        repository.Saved.Select(record => record.Id).Should().Equal(first.Id, latest.Id);
    }

    private static FormRecord CreateRecord(string company) => FormRecord.CreateDraft(
        "production-daily", 1, new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7),
        new Dictionary<string, string> { ["company"] = company }, "{}", company, DateTimeOffset.UtcNow);

    private sealed class RecordingRepository : IRecordRepository
    {
        public List<FormRecord> Saved { get; } = [];

        public Task SaveAsync(FormRecord record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Saved.Add(record);
            return Task.CompletedTask;
        }

        public Task<FormRecord?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<FormRecord?>(null);

        public Task<PagedResult> SearchAsync(RecordQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult([], 0, query.Page, query.PageSize));

        public Task<IReadOnlyList<FormRecord>> GetActiveRecordsSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FormRecord>>([]);
    }

    private sealed class BlockingRepository : IRecordRepository
    {
        private readonly TaskCompletionSource<bool> _saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<FormRecord> _saved = [];

        public TaskCompletionSource<bool> SaveStarted => _saveStarted;

        public IReadOnlyList<FormRecord> Saved
        {
            get
            {
                lock (_saved)
                {
                    return _saved.ToArray();
                }
            }
        }

        public async Task SaveAsync(FormRecord record, CancellationToken cancellationToken)
        {
            lock (_saved)
            {
                _saved.Add(record);
            }

            _saveStarted.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseSave() => _release.TrySetResult(true);

        public Task<FormRecord?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<FormRecord?>(null);

        public Task<PagedResult> SearchAsync(RecordQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult([], 0, query.Page, query.PageSize));

        public Task<IReadOnlyList<FormRecord>> GetActiveRecordsSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FormRecord>>([]);
    }
}
