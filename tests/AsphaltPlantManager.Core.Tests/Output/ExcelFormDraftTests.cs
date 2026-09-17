using AsphaltPlantManager.Core.Output;
using Xunit;

namespace AsphaltPlantManager.Core.Tests.Output;

public sealed class ExcelFormDraftTests
{
    [Fact]
    public void Imported_form_requires_a_template_identity_and_payload()
    {
        var result = new ImportedExcelForm(
            "asphalt-inventory",
            1,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 1),
            new Dictionary<string, string> { ["specification"] = "AC-25" },
            "{\"rows\":[{\"specification\":\"AC-25\"}]}",
            "AC-25");

        Assert.Equal("asphalt-inventory", result.TemplateId);
        Assert.Equal(1, result.TemplateVersion);
        Assert.Contains("AC-25", result.PayloadJson);
        Assert.Equal("AC-25", result.SearchText);
    }

    [Fact]
    public void Imported_form_rejects_blank_identity()
    {
        Assert.Throws<ArgumentException>(() => new ImportedExcelForm(
            " ",
            1,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 1),
            new Dictionary<string, string>(),
            "{}",
            ""));
    }

    [Fact]
    public void Imported_archive_edit_keeps_record_identity_and_version()
    {
        var recordId = Guid.NewGuid();
        var result = new ImportedExcelForm(
            "asphalt-inventory",
            1,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 1),
            new Dictionary<string, string>(),
            "{\"rows\":[]}",
            "",
            recordId,
            "GHC-202608-0001",
            2);

        Assert.Equal(recordId, result.RecordId);
        Assert.Equal("GHC-202608-0001", result.ArchiveNumber);
        Assert.Equal(2, result.ArchiveVersion);
    }
}
