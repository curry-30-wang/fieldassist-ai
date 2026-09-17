namespace AsphaltPlantManager.Core.Output;

public sealed record ImportedExcelForm
{
    public ImportedExcelForm(
        string templateId,
        int templateVersion,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyDictionary<string, string> headerSnapshot,
        string payloadJson,
        string searchText,
        Guid? recordId = null,
        string? archiveNumber = null,
        int? archiveVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        if (templateVersion <= 0) throw new ArgumentOutOfRangeException(nameof(templateVersion));
        ArgumentNullException.ThrowIfNull(headerSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        TemplateId = templateId;
        TemplateVersion = templateVersion;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        HeaderSnapshot = new Dictionary<string, string>(headerSnapshot, StringComparer.Ordinal);
        PayloadJson = payloadJson;
        SearchText = searchText ?? string.Empty;
        if (archiveVersion is <= 0) throw new ArgumentOutOfRangeException(nameof(archiveVersion));
        if (archiveNumber is not null) ArgumentException.ThrowIfNullOrWhiteSpace(archiveNumber);
        RecordId = recordId;
        ArchiveNumber = archiveNumber;
        ArchiveVersion = archiveVersion;
    }

    public string TemplateId { get; }
    public int TemplateVersion { get; }
    public DateOnly PeriodStart { get; }
    public DateOnly PeriodEnd { get; }
    public IReadOnlyDictionary<string, string> HeaderSnapshot { get; }
    public string PayloadJson { get; }
    public string SearchText { get; }
    public Guid? RecordId { get; }
    public string? ArchiveNumber { get; }
    public int? ArchiveVersion { get; }
}

public sealed record ExcelFormDraft
{
    public ExcelFormDraft(string path, string templateId, int templateVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        if (templateVersion <= 0) throw new ArgumentOutOfRangeException(nameof(templateVersion));
        Path = path;
        TemplateId = templateId;
        TemplateVersion = templateVersion;
    }

    public string Path { get; }
    public string TemplateId { get; }
    public int TemplateVersion { get; }
}
