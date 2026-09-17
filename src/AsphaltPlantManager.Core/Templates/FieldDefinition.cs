using System.Text.Json.Serialization;

namespace AsphaltPlantManager.Core.Templates;

public sealed record FieldDefinition(
    string Key,
    string Label,
    FieldDataType DataType,
    bool Required = false,
    object? DefaultValue = null,
    IReadOnlyList<string>? Options = null,
    bool IsSearchable = false,
    [property: JsonPropertyName("isSummable")] bool IsSummable = false);

public enum FieldDataType
{
    Text,
    Date,
    Decimal,
    Integer,
    Select
}

public sealed record ValidationError(string FieldKey, string Message);
