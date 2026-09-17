using AsphaltPlantManager.Core.Records;
using System.Text.Json;

namespace AsphaltPlantManager.Core.Search;

public sealed class SearchResult
{
    public SearchResult(FormRecord record)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        Customer = GetHeaderValue(record, "customer") ?? GetPayloadValue(record, "customer");
        Project = GetHeaderValue(record, "project") ?? GetHeaderValue(record, "projectPart") ?? GetPayloadValue(record, "project") ?? GetPayloadValue(record, "projectPart");
        Specification = GetHeaderValue(record, "specification") ?? GetPayloadValue(record, "specification");
    }

    public FormRecord Record { get; }
    public Guid Id => Record.Id;
    public string TemplateId => Record.TemplateId;
    public DateOnly PeriodStart => Record.PeriodStart;
    public DateOnly PeriodEnd => Record.PeriodEnd;
    public RecordStatus Status => Record.Status;
    public string? ArchiveNumber => Record.ArchiveNumber;
    public string? Customer { get; }
    public string? Project { get; }
    public string? Specification { get; }

    private static string? GetHeaderValue(FormRecord record, string key)
    {
        if (record.HeaderSnapshot.TryGetValue(key, out var headerValue) && !string.IsNullOrEmpty(headerValue))
        {
            return headerValue;
        }

        return null;
    }

    private static string? GetPayloadValue(FormRecord record, string key)
    {
        try
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(key, out var payloadValue))
            {
                return payloadValue.ValueKind == JsonValueKind.String ? payloadValue.GetString() : payloadValue.ToString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
