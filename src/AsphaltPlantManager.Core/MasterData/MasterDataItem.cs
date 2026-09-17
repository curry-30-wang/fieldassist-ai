namespace AsphaltPlantManager.Core.MasterData;

public sealed record MasterDataItem(string Category, string Key, string Value, DateTimeOffset? UpdatedAt = null);
