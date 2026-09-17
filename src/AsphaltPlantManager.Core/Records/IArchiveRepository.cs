namespace AsphaltPlantManager.Core.Records;

public interface IArchiveRepository
{
    Task<FormRecord?> GetAsync(Guid recordId, CancellationToken cancellationToken);
    Task<ArchiveVersion> SealAsync(Guid recordId, string archivePrefix, string changeNote, DateTimeOffset now, CancellationToken cancellationToken);
    Task<ArchiveVersion> SaveArchivedEditAsync(Guid recordId, string payloadJson, IReadOnlyDictionary<string, string> headerSnapshot, string changeNote, DateTimeOffset now, CancellationToken cancellationToken);
    Task<ArchiveVersion> RestoreVersionAsync(Guid recordId, int version, string changeNote, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArchiveVersion>> GetVersionsAsync(Guid recordId, CancellationToken cancellationToken);
    Task<bool> MoveToTrashAsync(Guid recordId, DateTimeOffset deletedAt, CancellationToken cancellationToken);
    Task<bool> RestoreFromTrashAsync(Guid recordId, CancellationToken cancellationToken);
}
