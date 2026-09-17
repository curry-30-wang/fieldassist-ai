namespace AsphaltPlantManager.Core.Dashboard;

public interface ISettingsReader
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);
}
