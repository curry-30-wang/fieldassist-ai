namespace AsphaltPlantManager.Core.Records;

using AsphaltPlantManager.Core.Templates;

public sealed class ArchiveService
{
    private const string LockedMessage = "当前应用会话尚未解锁归档编辑。";
    private readonly IArchiveRepository _repository;
    private readonly ArchiveSessionService _session;
    private readonly ITemplateCatalog _templates;
    private readonly ArchiveNumberService _numbers;

    public ArchiveService(IArchiveRepository repository, ArchiveSessionService session, ITemplateCatalog templates, ArchiveNumberService? numbers = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _numbers = numbers ?? new ArchiveNumberService();
    }

    public async Task<ArchiveVersion> SealAsync(Guid recordId, string changeNote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var record = await _repository.GetAsync(recordId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("未找到要封存的表单。");
        if (record.Status != RecordStatus.Completed)
        {
            throw new InvalidOperationException("仅已完成表单可首次封存。");
        }

        TemplateDefinition template;
        try
        {
            template = await _templates.GetAsync(record.TemplateId, record.TemplateVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidOperationException("未找到归档记录对应的历史模板。", exception);
        }

        return await _repository.SealAsync(recordId, _numbers.GetPrefix(template), changeNote, now, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ArchiveVersion>> GetVersionsAsync(Guid recordId, CancellationToken cancellationToken) => _repository.GetVersionsAsync(recordId, cancellationToken);

    public Task<ArchiveVersion> SaveArchivedEditAsync(Guid recordId, string payloadJson, IReadOnlyDictionary<string, string> headerSnapshot, string changeNote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureUnlocked();
        return _repository.SaveArchivedEditAsync(recordId, payloadJson, headerSnapshot, changeNote, now, cancellationToken);
    }

    public Task<ArchiveVersion> RestoreVersionAsync(Guid recordId, int version, string changeNote, DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureUnlocked();
        return _repository.RestoreVersionAsync(recordId, version, changeNote, now, cancellationToken);
    }

    private void EnsureUnlocked()
    {
        if (!_session.IsUnlocked)
        {
            throw new InvalidOperationException(LockedMessage);
        }
    }
}
