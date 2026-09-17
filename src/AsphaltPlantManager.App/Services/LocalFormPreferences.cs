using AsphaltPlantManager.Core.MasterData;
using System.Text.Json;

namespace AsphaltPlantManager.App;

public sealed class LocalFormPreferences
{
    private const string Category = "系统偏好";
    private const string FavoriteTemplatesKey = "favorite_templates";
    private const string SpecificationsKey = "specifications";
    private readonly IMasterDataRepository _masterData;

    public LocalFormPreferences(IMasterDataRepository masterData) => _masterData = masterData;

    public async Task<IReadOnlyList<string>> GetFavoriteTemplateIdsAsync(CancellationToken cancellationToken)
        => await ReadListAsync(FavoriteTemplatesKey, cancellationToken).ConfigureAwait(false);

    public Task SaveFavoriteTemplateIdsAsync(IEnumerable<string> templateIds, CancellationToken cancellationToken)
        => WriteListAsync(FavoriteTemplatesKey, templateIds, cancellationToken);

    public async Task<IReadOnlyList<string>> GetSpecificationsAsync(CancellationToken cancellationToken)
        => await ReadListAsync(SpecificationsKey, cancellationToken).ConfigureAwait(false);

    public Task RememberSpecificationsAsync(IEnumerable<string> specifications, CancellationToken cancellationToken)
        => WriteListAsync(SpecificationsKey, specifications, cancellationToken);

    private async Task<IReadOnlyList<string>> ReadListAsync(string key, CancellationToken cancellationToken)
    {
        var item = await _masterData.GetAsync(Category, key, cancellationToken).ConfigureAwait(false);
        if (item is null || string.IsNullOrWhiteSpace(item.Value)) return [];
        try
        {
            using var document = JsonDocument.Parse(item.Value);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteListAsync(string key, IEnumerable<string> values, CancellationToken cancellationToken)
    {
        var normalized = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        await _masterData.UpsertAsync(new MasterDataItem(Category, key, JsonSerializer.Serialize(normalized)), cancellationToken).ConfigureAwait(false);
    }
}
