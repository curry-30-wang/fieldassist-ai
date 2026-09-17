using AsphaltPlantManager.Core.Records;
using AsphaltPlantManager.Core.Templates;
using FluentAssertions;
using Xunit;

namespace AsphaltPlantManager.Core.Tests.Records;

public sealed class ArchiveServiceTests
{
    [Theory]
    [InlineData("production-daily", "SC")]
    [InlineData("asphalt-inventory", "GHC")]
    [InlineData("reconciliation-monthly", "DZ")]
    public async Task Seal_uses_the_exact_historic_template_archive_prefix(string templateId, string expectedPrefix)
    {
        var repository = new InMemoryArchiveRepository();
        var record = CompletedRecord(templateId);
        repository.Add(record);
        var service = CreateService(repository);

        var version = await service.SealAsync(record.Id, "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);

        version.ArchiveNumber.Should().Be($"{expectedPrefix}-202608-0001");
    }

    [Fact]
    public async Task Archive_sequences_are_independent_per_template_prefix()
    {
        var repository = new InMemoryArchiveRepository();
        var production = CompletedRecord("production-daily");
        var asphalt = CompletedRecord("asphalt-inventory");
        repository.Add(production);
        repository.Add(asphalt);
        var service = CreateService(repository);

        var productionVersion = await service.SealAsync(production.Id, "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);
        var asphaltVersion = await service.SealAsync(asphalt.Id, "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);

        productionVersion.ArchiveNumber.Should().Be("SC-202608-0001");
        asphaltVersion.ArchiveNumber.Should().Be("GHC-202608-0001");
    }

    [Fact]
    public async Task Missing_or_blank_template_prefix_does_not_allocate_an_archive_number()
    {
        var repository = new InMemoryArchiveRepository();
        var record = CompletedRecord();
        repository.Add(record);
        var service = new ArchiveService(repository, new ArchiveSessionService(new AcceptingPasswordStore()), new EmptyPrefixCatalog());

        var seal = () => service.SealAsync(record.Id, "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);

        await seal.Should().ThrowAsync<InvalidOperationException>();
        record.ArchiveNumber.Should().BeNull();
        (await service.GetVersionsAsync(record.Id, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_historic_template_does_not_allocate_an_archive_number()
    {
        var repository = new InMemoryArchiveRepository();
        var record = CompletedRecord("unknown-template");
        repository.Add(record);
        var service = CreateService(repository);

        var seal = () => service.SealAsync(record.Id, "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);

        await seal.Should().ThrowAsync<InvalidOperationException>();
        record.ArchiveNumber.Should().BeNull();
        (await service.GetVersionsAsync(record.Id, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Saving_an_archived_edit_requires_an_unlocked_session()
    {
        var repository = new InMemoryArchiveRepository();
        var record = CompletedRecord();
        repository.Add(record);
        var session = new ArchiveSessionService(new AcceptingPasswordStore());
        var service = new ArchiveService(repository, session, new TemplateCatalog());
        await service.SealAsync(record.Id, "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);

        var save = () => service.SaveArchivedEditAsync(record.Id, "{\"quantity\":20}", new Dictionary<string, string> { ["company"] = "new" }, "edit", DateTimeOffset.Parse("2026-08-07T09:00:00+08:00"), CancellationToken.None);

        await save.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Restoring_a_historic_version_appends_a_new_version_without_changing_archive_number()
    {
        var repository = new InMemoryArchiveRepository();
        var record = CompletedRecord();
        repository.Add(record);
        var session = new ArchiveSessionService(new AcceptingPasswordStore());
        var service = new ArchiveService(repository, session, new TemplateCatalog());
        await service.SealAsync(record.Id, "seal", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), CancellationToken.None);
        await session.UnlockAsync("password", CancellationToken.None);
        await service.SaveArchivedEditAsync(record.Id, "{\"quantity\":20}", new Dictionary<string, string> { ["company"] = "new" }, "edit", DateTimeOffset.Parse("2026-08-07T09:00:00+08:00"), CancellationToken.None);

        var restored = await service.RestoreVersionAsync(record.Id, 1, "restore", DateTimeOffset.Parse("2026-08-07T10:00:00+08:00"), CancellationToken.None);

        restored.Version.Should().Be(3);
        restored.ArchiveNumber.Should().Be("SC-202608-0001");
        (await service.GetVersionsAsync(record.Id, CancellationToken.None)).Select(v => v.PayloadJson).Should().Equal("{\"quantity\":10}", "{\"quantity\":20}", "{\"quantity\":10}");
    }

    private static ArchiveService CreateService(InMemoryArchiveRepository repository) => new(repository, new ArchiveSessionService(new AcceptingPasswordStore()), new TemplateCatalog());

    private static FormRecord CompletedRecord(string templateId = "production-daily")
    {
        var record = FormRecord.CreateDraft(templateId, 1, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), new Dictionary<string, string> { ["company"] = "old", ["archivePrefix"] = "WRONG" }, "{\"quantity\":10}", "old", DateTimeOffset.Parse("2026-08-07T07:00:00+08:00"));
        record.MarkCompleted();
        return record;
    }

    private sealed class AcceptingPasswordStore : IUnlockPasswordStore
    {
        public Task<bool> HasPasswordAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task SetPasswordAsync(string password, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> VerifyAsync(string password, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class TemplateCatalog : ITemplateCatalog
    {
        private static readonly IReadOnlyDictionary<string, string> Prefixes = new Dictionary<string, string>
        {
            ["production-daily"] = "SC",
            ["asphalt-inventory"] = "GHC",
            ["reconciliation-monthly"] = "DZ"
        };

        public Task<TemplateDefinition> GetAsync(string id, int? version, CancellationToken cancellationToken) =>
            Prefixes.TryGetValue(id, out var prefix)
                ? Task.FromResult(new TemplateDefinition(id, id, "test", version ?? 1, prefix, [], [], [], new PrintLayout("A4", "Portrait")))
                : throw new KeyNotFoundException();

        public Task<IReadOnlyList<TemplateDefinition>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TemplateDefinition>>([]);
    }

    private sealed class EmptyPrefixCatalog : ITemplateCatalog
    {
        public Task<TemplateDefinition> GetAsync(string id, int? version, CancellationToken cancellationToken) => Task.FromResult(new TemplateDefinition(id, id, "test", version ?? 1, " ", [], [], [], new PrintLayout("A4", "Portrait")));
        public Task<IReadOnlyList<TemplateDefinition>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TemplateDefinition>>([]);
    }

    private sealed class InMemoryArchiveRepository : IArchiveRepository
    {
        private readonly Dictionary<Guid, FormRecord> _records = [];
        private readonly Dictionary<Guid, List<ArchiveVersion>> _versions = [];
        private readonly Dictionary<string, int> _sequences = [];
        public void Add(FormRecord record) => _records.Add(record.Id, record);
        public Task<FormRecord?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_records.GetValueOrDefault(id));
        public Task<ArchiveVersion> SealAsync(Guid recordId, string archivePrefix, string changeNote, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var record = _records[recordId];
            var key = $"{archivePrefix}-{now:yyyyMM}";
            _sequences[key] = _sequences.GetValueOrDefault(key) + 1;
            record.Seal($"{key}-{_sequences[key]:0000}", changeNote, now);
            return Task.FromResult(AddVersion(record, changeNote, now));
        }
        public Task<ArchiveVersion> SaveArchivedEditAsync(Guid recordId, string payloadJson, IReadOnlyDictionary<string, string> headerSnapshot, string changeNote, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var record = _records[recordId];
            record.UpdateArchivedContent(headerSnapshot, payloadJson, now);
            return Task.FromResult(AddVersion(record, changeNote, now));
        }
        public Task<ArchiveVersion> RestoreVersionAsync(Guid recordId, int version, string changeNote, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var previous = _versions[recordId].Single(v => v.Version == version);
            var record = _records[recordId];
            record.UpdateArchivedContent(previous.HeaderSnapshot, previous.PayloadJson, now);
            return Task.FromResult(AddVersion(record, changeNote, now));
        }
        public Task<IReadOnlyList<ArchiveVersion>> GetVersionsAsync(Guid recordId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ArchiveVersion>>(_versions.GetValueOrDefault(recordId, []).OrderBy(v => v.Version).ToArray());
        public Task<bool> MoveToTrashAsync(Guid recordId, DateTimeOffset deletedAt, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> RestoreFromTrashAsync(Guid recordId, CancellationToken cancellationToken) => Task.FromResult(false);
        private ArchiveVersion AddVersion(FormRecord record, string note, DateTimeOffset now)
        {
            var version = new ArchiveVersion(record.Id, _versions.GetValueOrDefault(record.Id, []).Count + 1, record.ArchiveNumber!, record.TemplateId, record.TemplateVersion, record.HeaderSnapshot, record.PayloadJson, note, now);
            if (!_versions.TryGetValue(record.Id, out var versions)) _versions[record.Id] = versions = [];
            versions.Add(version);
            return version;
        }
    }
}
