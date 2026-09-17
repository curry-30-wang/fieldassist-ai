using System.Collections.ObjectModel;
using System.Text.Json;
using AsphaltPlantManager.Core.Templates;

namespace AsphaltPlantManager.Core.Output;

public sealed class OutputRecordSnapshot
{
    public OutputRecordSnapshot(
        string archiveNumber,
        int version,
        TemplateDefinition template,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyDictionary<string, string> companyHeader,
        string payloadJson,
        string changeNote,
        DateTimeOffset versionedAt,
        DateTimeOffset printedAt,
        Guid? recordId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(companyHeader);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeNote);
        if (periodEnd < periodStart)
        {
            throw new ArgumentException("业务期间结束日期不能早于开始日期。", nameof(periodEnd));
        }

        ArchiveNumber = archiveNumber.Trim();
        Version = version;
        Template = new OutputTemplateSnapshot(template);
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        CompanyHeader = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(companyHeader, StringComparer.Ordinal));
        PayloadJson = payloadJson;
        ChangeNote = changeNote;
        VersionedAt = versionedAt;
        PrintedAt = printedAt;
        RecordId = recordId;
    }

    public string ArchiveNumber { get; }
    public int Version { get; }
    public OutputTemplateSnapshot Template { get; }
    public DateOnly PeriodStart { get; }
    public DateOnly PeriodEnd { get; }
    public IReadOnlyDictionary<string, string> CompanyHeader { get; }
    public string PayloadJson { get; }
    public string ChangeNote { get; }
    public DateTimeOffset VersionedAt { get; }
    public DateTimeOffset PrintedAt { get; }
    public Guid? RecordId { get; }
}

public sealed class OutputTemplateSnapshot
{
    internal OutputTemplateSnapshot(TemplateDefinition source)
    {
        Id = source.Id;
        Name = source.Name;
        Category = source.Category;
        Version = source.Version;
        ArchivePrefix = source.ArchivePrefix;
        Fields = Array.AsReadOnly(source.Fields.Select(field => new OutputFieldSnapshot(field)).ToArray());
        Formulas = Array.AsReadOnly(source.Formulas.Select(formula => new OutputFormulaSnapshot(formula)).ToArray());
        QueryFields = Array.AsReadOnly(source.QueryFields.ToArray());
        PrintLayout = new OutputPrintLayoutSnapshot(source.PrintLayout);
    }

    public string Id { get; }
    public string Name { get; }
    public string Category { get; }
    public int Version { get; }
    public string ArchivePrefix { get; }
    public IReadOnlyList<OutputFieldSnapshot> Fields { get; }
    public IReadOnlyList<OutputFormulaSnapshot> Formulas { get; }
    public IReadOnlyList<string> QueryFields { get; }
    public OutputPrintLayoutSnapshot PrintLayout { get; }
}

public sealed class OutputFieldSnapshot
{
    internal OutputFieldSnapshot(FieldDefinition source)
    {
        Key = source.Key;
        Label = source.Label;
        DataType = source.DataType;
        Required = source.Required;
        DefaultValue = FreezeDefaultValue(source.DefaultValue);
        Options = source.Options is null ? null : Array.AsReadOnly(source.Options.ToArray());
        IsSearchable = source.IsSearchable;
        IsSummable = source.IsSummable;
    }

    public string Key { get; }
    public string Label { get; }
    public FieldDataType DataType { get; }
    public bool Required { get; }
    public object? DefaultValue { get; }
    public IReadOnlyList<string>? Options { get; }
    public bool IsSearchable { get; }
    public bool IsSummable { get; }

    private static object? FreezeDefaultValue(object? value) => value switch
    {
        null => null,
        JsonElement element => element.Clone(),
        string or bool or char or byte or sbyte or short or ushort or int or uint or long or ulong or
            float or double or decimal or DateOnly or TimeOnly or DateTime or DateTimeOffset or Guid => value,
        Enum => value,
        _ => JsonSerializer.SerializeToElement(value).Clone()
    };
}

public sealed class OutputFormulaSnapshot
{
    internal OutputFormulaSnapshot(FormulaDefinition source)
    {
        Target = source.Target;
        Operator = source.Operator;
        Operands = Array.AsReadOnly(source.Operands.ToArray());
        DecimalPlaces = source.DecimalPlaces;
    }

    public string Target { get; }
    public FormulaOperator Operator { get; }
    public IReadOnlyList<string> Operands { get; }
    public int DecimalPlaces { get; }
}

public sealed class OutputPrintLayoutSnapshot
{
    internal OutputPrintLayoutSnapshot(PrintLayout source)
    {
        Paper = source.Paper;
        Orientation = source.Orientation;
    }

    public string Paper { get; }
    public string Orientation { get; }
}
