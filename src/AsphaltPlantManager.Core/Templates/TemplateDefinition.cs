using System.Text.Json.Serialization;

namespace AsphaltPlantManager.Core.Templates;

public sealed record TemplateDefinition(
    string Id,
    string Name,
    string Category,
    int Version,
    string ArchivePrefix,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<FormulaDefinition> Formulas,
    [property: JsonPropertyName("searchFields")] IReadOnlyList<string> QueryFields,
    PrintLayout PrintLayout);

public sealed record PrintLayout(string Paper, string Orientation);
