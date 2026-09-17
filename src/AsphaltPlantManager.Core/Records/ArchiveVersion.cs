namespace AsphaltPlantManager.Core.Records;

public sealed class ArchiveVersion
{
    public ArchiveVersion(
        Guid recordId,
        int version,
        string archiveNumber,
        string templateId,
        int templateVersion,
        IReadOnlyDictionary<string, string> headerSnapshot,
        string payloadJson,
        string changeNote,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(templateVersion);
        ArgumentNullException.ThrowIfNull(headerSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeNote);
        RecordId = recordId;
        Version = version;
        ArchiveNumber = archiveNumber;
        TemplateId = templateId;
        TemplateVersion = templateVersion;
        HeaderSnapshot = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(headerSnapshot, StringComparer.Ordinal));
        PayloadJson = payloadJson;
        ChangeNote = changeNote;
        CreatedAt = createdAt;
    }

    public Guid RecordId { get; }
    public int Version { get; }
    public string ArchiveNumber { get; }
    public string TemplateId { get; }
    public int TemplateVersion { get; }
    public IReadOnlyDictionary<string, string> HeaderSnapshot { get; }
    public string PayloadJson { get; }
    public string ChangeNote { get; }
    public DateTimeOffset CreatedAt { get; }
}
