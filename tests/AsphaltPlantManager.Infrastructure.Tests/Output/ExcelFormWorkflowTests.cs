using AsphaltPlantManager.Infrastructure.Output;
using AsphaltPlantManager.Infrastructure.Templates;
using AsphaltPlantManager.Core.MasterData;
using AsphaltPlantManager.Core.Output;
using ClosedXML.Excel;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Output;

public sealed class ExcelFormWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"AsphaltPlantManager-Excel-{Guid.NewGuid():N}");

    [Fact]
    public async Task Creates_an_editable_workbook_with_metadata_rows_and_formula_cells()
    {
        var catalog = new JsonTemplateCatalog(FindTemplatesDirectory());
        var template = await catalog.GetAsync("asphalt-inventory", 1, default);
        var workflow = new ExcelFormWorkflow(catalog);

        var draft = await workflow.CreateDraftAsync(
            template, "测试公司", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), _directory, default);

        using var workbook = new XLWorkbook(draft.Path);
        var sheet = workbook.Worksheets.Single(item => item.Name == template.Name);
        var meta = workbook.Worksheets.Single(item => item.Name == "__FormMeta");

        Assert.Equal("asphalt-inventory", meta.Cell("B1").GetString());
        Assert.Equal("测试公司", meta.Cell("B5").GetString());
        Assert.Equal(template.Name, sheet.Cell("A1").GetString());
        Assert.Contains(sheet.CellsUsed().Where(cell => cell.HasFormula).Select(cell => cell.FormulaA1),
            formula => formula.Contains('+') || formula.Contains('-'));
        Assert.True(sheet.AutoFilter.IsEnabled);
        Assert.True(sheet.LastRowUsed()!.RowNumber() >= 35);
    }

    [Fact]
    public async Task Imports_multiple_rows_and_accepts_custom_specification_text()
    {
        var catalog = new JsonTemplateCatalog(FindTemplatesDirectory());
        var template = await catalog.GetAsync("asphalt-inventory", 1, default);
        var workflow = new ExcelFormWorkflow(catalog);
        var draft = await workflow.CreateDraftAsync(
            template, "测试公司", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), _directory, default);

        using (var workbook = new XLWorkbook(draft.Path))
        {
            var sheet = workbook.Worksheets.Single(item => item.Name == template.Name);
            var column = template.Fields.Select((field, index) => (field.Key, Column: index + 1))
                .ToDictionary(item => item.Key, item => item.Column);
            sheet.Cell(6, column["date"]).Value = new DateTime(2026, 8, 1);
            sheet.Cell(6, column["specification"]).Value = "AC-25 自定义";
            sheet.Cell(6, column["opening"]).Value = 10;
            sheet.Cell(6, column["purchase"]).Value = 20;
            sheet.Cell(6, column["consumption"]).Value = 5;
            sheet.Cell(7, column["date"]).Value = new DateTime(2026, 8, 1);
            sheet.Cell(7, column["specification"]).Value = "SMA-20 新规格";
            sheet.Cell(7, column["opening"]).Value = 2;
            sheet.Cell(7, column["purchase"]).Value = 3;
            sheet.Cell(7, column["consumption"]).Value = 1;
            workbook.Save();
        }

        var imported = await workflow.ImportAsync(draft.Path, default);
        Assert.Equal("asphalt-inventory", imported.TemplateId);
        Assert.Contains("AC-25", imported.PayloadJson);
        Assert.Contains("SMA-20", imported.PayloadJson);
        Assert.Contains("SMA-20 新规格", imported.SearchText);
    }

    [Fact]
    public async Task Creates_editable_archive_workbook_and_round_trips_record_identity()
    {
        var catalog = new JsonTemplateCatalog(FindTemplatesDirectory());
        var template = await catalog.GetAsync("asphalt-inventory", 1, default);
        var recordId = Guid.NewGuid();
        var snapshot = new OutputRecordSnapshot(
            "GHC-202608-0001",
            2,
            template,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 1),
            new Dictionary<string, string> { ["company"] = "测试公司" },
            "{\"rows\":[{\"date\":\"2026-08-01\",\"specification\":\"AC-25\",\"opening\":10,\"purchase\":20,\"consumption\":5}]}",
            "修正已封存档案",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            recordId);
        var workflow = new ExcelFormWorkflow(catalog);

        var draft = await workflow.CreateEditableDraftAsync(snapshot, _directory, default);
        using (var workbook = new XLWorkbook(draft.Path))
        {
            var meta = workbook.Worksheets.Single(item => item.Name == "__FormMeta");
            Assert.Equal(recordId.ToString("D"), meta.Cell("B8").GetString());
            Assert.Equal("GHC-202608-0001", meta.Cell("B9").GetString());
            Assert.Equal(2, meta.Cell("B10").GetValue<int>());
        }

        var imported = await workflow.ImportAsync(draft.Path, default);
        Assert.Equal(recordId, imported.RecordId);
        Assert.Equal("GHC-202608-0001", imported.ArchiveNumber);
        Assert.Equal(2, imported.ArchiveVersion);
    }

    [Fact]
    public async Task Adds_common_customer_vehicle_and_driver_lists_to_excel_template()
    {
        var catalog = new JsonTemplateCatalog(FindTemplatesDirectory());
        var template = await catalog.GetAsync("finished-goods-dispatch", 1, default);
        var commonData = new StubMasterDataRepository(
            new MasterDataItem("客户", "甲客户", ""),
            new MasterDataItem("车辆", "沪A12345", ""),
            new MasterDataItem("司机", "张师傅", ""));
        var workflow = new ExcelFormWorkflow(catalog, commonData);

        var draft = await workflow.CreateDraftAsync(template, "测试公司", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), _directory, default);

        using var workbook = new XLWorkbook(draft.Path);
        var listSheet = workbook.Worksheets.Single(item => item.Name == "__CommonData");
        var sheet = workbook.Worksheets.First(item => item.Name is not "__FormMeta" and not "__CommonData");
        Assert.Equal("customer", listSheet.Cell(1, 1).GetString());
        Assert.Contains("甲客户", listSheet.CellsUsed().Select(cell => cell.GetString()));
        Assert.True(sheet.DataValidations.Count() > 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static string FindTemplatesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AsphaltPlantManager.sln")))
                return Path.Combine(directory.FullName, "templates");
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 templates 目录。");
    }

    private sealed class StubMasterDataRepository(params MasterDataItem[] items) : IMasterDataRepository
    {
        public Task UpsertAsync(MasterDataItem item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MasterDataItem?> GetAsync(string category, string key, CancellationToken cancellationToken) => Task.FromResult<MasterDataItem?>(items.SingleOrDefault(item => item.Category == category && item.Key == key));
        public Task<IReadOnlyList<MasterDataItem>> ListAsync(string category, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MasterDataItem>>(items.Where(item => item.Category == category).ToArray());
        public Task<bool> DeleteAsync(string category, string key, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
