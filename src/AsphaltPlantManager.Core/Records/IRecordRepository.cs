namespace AsphaltPlantManager.Core.Records;

using AsphaltPlantManager.Core.Search;

public interface IRecordRepository
{
    Task SaveAsync(FormRecord record, CancellationToken cancellationToken);

    Task<FormRecord?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult> SearchAsync(RecordQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormRecord>> GetActiveRecordsSnapshotAsync(CancellationToken cancellationToken);
}
