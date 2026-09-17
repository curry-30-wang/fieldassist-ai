namespace AsphaltPlantManager.Core.Records;

public sealed class TrashService
{
    private readonly IArchiveRepository _repository;
    public TrashService(IArchiveRepository repository) => _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    public Task<bool> MoveAsync(Guid recordId, DateTimeOffset deletedAt, CancellationToken cancellationToken) => _repository.MoveToTrashAsync(recordId, deletedAt, cancellationToken);
    public Task<bool> RestoreAsync(Guid recordId, CancellationToken cancellationToken) => _repository.RestoreFromTrashAsync(recordId, cancellationToken);
}
