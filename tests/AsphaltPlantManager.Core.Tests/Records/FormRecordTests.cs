using AsphaltPlantManager.Core.Records;
using FluentAssertions;
using Xunit;

namespace AsphaltPlantManager.Core.Tests.Records;

public sealed class FormRecordTests
{
    [Fact]
    public void Sealed_record_must_be_unsealed_before_editing()
    {
        var record = FormRecord.CreateDraft("asphalt-inventory", 1, new(2026, 8, 1), new(2026, 8, 31));
        record.MarkCompleted();
        record.Seal("GHC-202608-0001", "首次封存", DateTimeOffset.Parse("2026-08-31T10:00:00+08:00"));
        record.CanEdit.Should().BeFalse();
        record.Unseal();
        record.CanEdit.Should().BeTrue();
    }

    [Fact]
    public void Only_a_sealed_record_can_be_unsealed()
    {
        var record = FormRecord.CreateDraft("asphalt-inventory", 1, new(2026, 8, 1), new(2026, 8, 31));

        Action unseal = record.Unseal;

        unseal.Should().Throw<InvalidOperationException>()
            .WithMessage("只有已封存表格可以解封。*");
    }

    [Fact]
    public void Sealed_record_cannot_be_marked_completed_until_unsealed()
    {
        var record = CreateSealedRecord();

        Action markCompleted = record.MarkCompleted;

        markCompleted.Should().Throw<InvalidOperationException>()
            .WithMessage("已封存表格必须先解封后才能修改。*");
    }

    [Fact]
    public void Sealed_record_cannot_be_sealed_until_unsealed()
    {
        var record = CreateSealedRecord();

        Action seal = () => record.Seal("GHC-202608-0002", "重复封存", DateTimeOffset.Parse("2026-08-31T11:00:00+08:00"));

        seal.Should().Throw<InvalidOperationException>()
            .WithMessage("已封存表格必须先解封后才能修改。*");
    }

    [Fact]
    public void Draft_and_seal_preserve_the_record_metadata()
    {
        var periodStart = new DateOnly(2026, 8, 1);
        var periodEnd = new DateOnly(2026, 8, 31);
        var sealedAt = DateTimeOffset.Parse("2026-08-31T10:00:00+08:00");
        var record = FormRecord.CreateDraft("asphalt-inventory", 1, periodStart, periodEnd);

        record.MarkCompleted();
        record.Seal("GHC-202608-0001", "首次封存", sealedAt);

        record.Id.Should().NotBeEmpty();
        record.TemplateId.Should().Be("asphalt-inventory");
        record.TemplateVersion.Should().Be(1);
        record.PeriodStart.Should().Be(periodStart);
        record.PeriodEnd.Should().Be(periodEnd);
        record.ArchiveNumber.Should().Be("GHC-202608-0001");
        record.ChangeNote.Should().Be("首次封存");
        record.SealedAt.Should().Be(sealedAt);
    }

    [Fact]
    public void Completed_record_cannot_have_its_content_replaced()
    {
        var record = FormRecord.CreateDraft("asphalt-inventory", 1, new(2026, 8, 1), new(2026, 8, 31));
        record.MarkCompleted();

        Action update = () => record.UpdateContent(
            new Dictionary<string, string> { ["company"] = "公司" }, "{}", "公司", DateTimeOffset.UtcNow);

        update.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void New_draft_uses_one_timestamp_for_creation_and_last_update()
    {
        var record = FormRecord.CreateDraft("asphalt-inventory", 1, new(2026, 8, 1), new(2026, 8, 31));

        record.UpdatedAt.Should().Be(record.CreatedAt);
    }

    [Fact]
    public void Restore_rejects_a_sealed_record_without_complete_archive_metadata()
    {
        Action restore = () => FormRecord.Restore(
            Guid.NewGuid(), "asphalt-inventory", 1, new(2026, 8, 1), new(2026, 8, 31),
            RecordStatus.Sealed, null, "首次封存", DateTimeOffset.UtcNow,
            new Dictionary<string, string>(), "{}", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);

        restore.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Restore_allows_an_unsealed_record_to_keep_its_archive_metadata()
    {
        var sealedAt = DateTimeOffset.Parse("2026-08-07T08:00:00.0000000+08:00");

        var record = FormRecord.Restore(
            Guid.NewGuid(), "asphalt-inventory", 1, new(2026, 8, 1), new(2026, 8, 31),
            RecordStatus.Unsealed, "GHC-202608-0001", "首次封存", sealedAt,
            new Dictionary<string, string>(), "{}", "", sealedAt, sealedAt, false);

        record.Status.Should().Be(RecordStatus.Unsealed);
        record.ArchiveNumber.Should().Be("GHC-202608-0001");
        record.ChangeNote.Should().Be("首次封存");
        record.SealedAt.Should().Be(sealedAt);
    }

    [Fact]
    public void Restore_rejects_a_completed_record_with_archive_metadata()
    {
        Action restore = () => FormRecord.Restore(
            Guid.NewGuid(), "asphalt-inventory", 1, new(2026, 8, 1), new(2026, 8, 31),
            RecordStatus.Completed, "GHC-202608-0001", "首次封存", DateTimeOffset.UtcNow,
            new Dictionary<string, string>(), "{}", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);

        restore.Should().Throw<ArgumentException>();
    }

    private static FormRecord CreateSealedRecord()
    {
        var record = FormRecord.CreateDraft("asphalt-inventory", 1, new(2026, 8, 1), new(2026, 8, 31));
        record.MarkCompleted();
        record.Seal("GHC-202608-0001", "首次封存", DateTimeOffset.Parse("2026-08-31T10:00:00+08:00"));
        return record;
    }
}
