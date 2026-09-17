namespace AsphaltPlantManager.Core.Records;

public sealed class FormRecord
{
    private FormRecord(
        Guid id,
        string templateId,
        int templateVersion,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyDictionary<string, string> headerSnapshot,
        string payloadJson,
        string searchText,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        TemplateId = templateId;
        TemplateVersion = templateVersion;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        HeaderSnapshot = CopySnapshot(headerSnapshot);
        PayloadJson = payloadJson;
        SearchText = searchText;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public string TemplateId { get; }

    public int TemplateVersion { get; }

    public DateOnly PeriodStart { get; }

    public DateOnly PeriodEnd { get; }

    public RecordStatus Status { get; private set; } = RecordStatus.Draft;

    public string? ArchiveNumber { get; private set; }

    public string? ChangeNote { get; private set; }

    public DateTimeOffset? SealedAt { get; private set; }

    public IReadOnlyDictionary<string, string> HeaderSnapshot { get; private set; }

    public string PayloadJson { get; private set; }

    public string SearchText { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    public bool CanEdit => Status is RecordStatus.Draft or RecordStatus.Unsealed;

    public static FormRecord CreateDraft(
        string templateId,
        int templateVersion,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyDictionary<string, string>? headerSnapshot = null,
        string payloadJson = "{}",
        string searchText = "",
        DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new FormRecord(
            Guid.NewGuid(),
            templateId,
            templateVersion,
            periodStart,
            periodEnd,
            headerSnapshot ?? new Dictionary<string, string>(),
            payloadJson,
            searchText,
            timestamp,
            timestamp);
    }

    internal static FormRecord Restore(
        Guid id,
        string templateId,
        int templateVersion,
        DateOnly periodStart,
        DateOnly periodEnd,
        RecordStatus status,
        string? archiveNumber,
        string? changeNote,
        DateTimeOffset? sealedAt,
        IReadOnlyDictionary<string, string> headerSnapshot,
        string payloadJson,
        string searchText,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        ValidateRestoredState(status, archiveNumber, changeNote, sealedAt);
        var record = new FormRecord(id, templateId, templateVersion, periodStart, periodEnd, headerSnapshot, payloadJson, searchText, createdAt, updatedAt)
        {
            Status = status,
            ArchiveNumber = archiveNumber,
            ChangeNote = changeNote,
            SealedAt = sealedAt,
            IsDeleted = isDeleted
        };
        return record;
    }

    public void UpdateContent(
        IReadOnlyDictionary<string, string> headerSnapshot,
        string payloadJson,
        string searchText,
        DateTimeOffset now)
    {
        EnsureCanEdit();
        HeaderSnapshot = CopySnapshot(headerSnapshot);
        PayloadJson = payloadJson;
        SearchText = searchText;
        UpdatedAt = now;
    }

    public void UpdateArchivedContent(
        IReadOnlyDictionary<string, string> headerSnapshot,
        string payloadJson,
        DateTimeOffset now)
    {
        if (Status != RecordStatus.Sealed)
        {
            throw new InvalidOperationException("Only sealed records can receive an archived edit.");
        }

        ArgumentNullException.ThrowIfNull(headerSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        HeaderSnapshot = CopySnapshot(headerSnapshot);
        PayloadJson = payloadJson;
        UpdatedAt = now;
    }

    public void MarkCompleted()
    {
        EnsureNotSealed();
        Status = RecordStatus.Completed;
    }

    public void Seal(string archiveNumber, string changeNote, DateTimeOffset now)
    {
        EnsureNotSealed();
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeNote);
        ArchiveNumber = archiveNumber;
        ChangeNote = changeNote;
        SealedAt = now;
        Status = RecordStatus.Sealed;
    }

    public void Unseal()
    {
        if (Status != RecordStatus.Sealed)
        {
            throw new InvalidOperationException("只有已封存表格可以解封。");
        }

        Status = RecordStatus.Unsealed;
    }

    private void EnsureNotSealed()
    {
        if (Status == RecordStatus.Sealed)
        {
            throw new InvalidOperationException("已封存表格必须先解封后才能修改。");
        }
    }

    private void EnsureCanEdit()
    {
        if (!CanEdit)
        {
            throw new InvalidOperationException("Only draft or unsealed records can be edited.");
        }
    }

    private static void ValidateRestoredState(
        RecordStatus status,
        string? archiveNumber,
        string? changeNote,
        DateTimeOffset? sealedAt)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var isArchivedState = status is RecordStatus.Sealed or RecordStatus.Unsealed;
        if (isArchivedState)
        {
            if (string.IsNullOrWhiteSpace(archiveNumber) || string.IsNullOrWhiteSpace(changeNote) || sealedAt is null)
            {
                throw new ArgumentException("Sealed and unsealed records require complete archive metadata.");
            }

            return;
        }

        if (archiveNumber is not null || changeNote is not null || sealedAt is not null)
        {
            throw new ArgumentException("Draft and completed records cannot contain archive metadata.");
        }
    }

    private static IReadOnlyDictionary<string, string> CopySnapshot(IReadOnlyDictionary<string, string> headerSnapshot) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(headerSnapshot, StringComparer.Ordinal));
}
