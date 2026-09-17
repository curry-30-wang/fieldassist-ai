namespace AsphaltPlantManager.Core.MasterData;

public interface IMasterDataRepository
{
    Task UpsertAsync(MasterDataItem item, CancellationToken cancellationToken);

    Task<MasterDataItem?> GetAsync(string category, string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<MasterDataItem>> ListAsync(string category, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string category, string key, CancellationToken cancellationToken);
}
