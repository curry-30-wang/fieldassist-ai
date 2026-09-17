using System.IO.Compression;
using System.Text;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Core.Templates;
using AsphaltPlantManager.Infrastructure.Output;
using ClosedXML.Excel;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using UglyToad.PdfPig;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Output;

public sealed class ExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"asphalt-output-{Guid.NewGuid():N}");

    [Fact]
    public async Task Excel_export_preserves_values_types_formulas_and_print_settings()
    {
        var path = await new ExcelRecordExporter().ExportAsync(CreateReconciliationSnapshot(1), _root, CancellationToken.None);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        sheet.Cell("A1").GetString().Should().Contain("对账单");
        sheet.CellsUsed().Select(cell => cell.GetFormattedString()).Should().Contain(text => text.Contains("DZ-202608-0001", StringComparison.Ordinal));
        sheet.CellsUsed().Select(cell => cell.GetFormattedString()).Should().Contain(text => text.Contains("v3", StringComparison.Ordinal));
        sheet.CellsUsed().Select(cell => cell.GetFormattedString()).Should().Contain(text => text.Contains("2026-08-07 08:00", StringComparison.Ordinal));
        sheet.CellsUsed().Select(cell => cell.GetFormattedString()).Should().Contain(text => text.Contains("2026-08-07 10:30", StringComparison.Ordinal));
        sheet.CellsUsed().Select(cell => cell.GetFormattedString()).Should().Contain("华东路桥");
        sheet.CellsUsed().Select(cell => cell.GetFormattedString()).Should().Contain("AC-13");
        sheet.CellsUsed().Any(cell => cell.GetFormattedString() == "238.14" && cell.DataType == XLDataType.Number).Should().BeTrue();
        sheet.CellsUsed().Any(cell => cell.GetFormattedString() == "135.30" && cell.DataType == XLDataType.Number).Should().BeTrue();
        sheet.CellsUsed().Should().Contain(cell => cell.HasFormula && cell.GetValue<decimal>() == 32220.34m);
        sheet.CellsUsed().Should().Contain(cell => cell.DataType == XLDataType.DateTime && cell.Style.DateFormat.Format == "yyyy-mm-dd");
        sheet.CellsUsed().Should().Contain(cell => cell.HasFormula && cell.FormulaA1.StartsWith("SUM(", StringComparison.OrdinalIgnoreCase));
        sheet.PageSetup.PaperSize.Should().Be(XLPaperSize.A4Paper);
        sheet.PageSetup.PageOrientation.Should().Be(XLPageOrientation.Landscape);
        sheet.SheetView.SplitRow.Should().BeGreaterThan(0);
        sheet.ShowGridLines.Should().BeFalse();
        workbook.Worksheets.SelectMany(item => item.MergedRanges).Should().NotContain(range =>
            range.RangeAddress.FirstAddress.RowNumber <= 7 && range.RangeAddress.LastAddress.RowNumber >= 5);

        using var archive = ZipFile.OpenRead(path);
        var workbookXml = ReadEntry(archive, "xl/workbook.xml");
        var sheetXml = ReadEntry(archive, "xl/worksheets/sheet1.xml");
        workbookXml.Should().Contain("_xlnm.Print_Titles").And.Contain("_xlnm.Print_Area");
        sheetXml.Should().Contain("paperSize=\"9\"").And.Contain("orientation=\"landscape\"").And.Contain("pageMargins");
    }

    [Fact]
    public async Task Excel_totals_include_a_summable_first_column_without_overwriting_it_with_the_label()
    {
        var snapshot = CreateFirstColumnSummableSnapshot();
        var path = await new ExcelRecordExporter().ExportAsync(snapshot, _root, CancellationToken.None);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        var totalLabel = sheet.CellsUsed().Single(cell => cell.GetString() == "合计");
        var firstColumnTotal = sheet.Cell(totalLabel.Address.RowNumber, 1);

        firstColumnTotal.HasFormula.Should().BeTrue();
        firstColumnTotal.FormulaA1.Should().StartWith("SUM(A");
        firstColumnTotal.GetValue<decimal>().Should().Be(6m);
        totalLabel.Address.ColumnNumber.Should().Be(2);
        new RecordLayoutBuilder().Build(snapshot).Totals.Should().ContainSingle("数量：6.00");
    }

    [Fact]
    public async Task Excel_all_summable_fields_use_a_dedicated_total_label_column()
    {
        var path = await new ExcelRecordExporter().ExportAsync(
            CreateAllColumnsSummableSnapshot(),
            _root,
            CancellationToken.None);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        var totalLabel = sheet.CellsUsed().Single(cell => cell.GetString() == "合计");
        var totalRow = totalLabel.Address.RowNumber;

        totalLabel.Address.ColumnNumber.Should().Be(3);
        sheet.Cell(totalRow, 1).FormulaA1.Should().StartWith("SUM(A");
        sheet.Cell(totalRow, 2).FormulaA1.Should().StartWith("SUM(B");
        sheet.Cell(totalRow, 1).GetValue<decimal>().Should().Be(6m);
        sheet.Cell(totalRow, 2).GetValue<decimal>().Should().Be(60m);
        sheet.MergedRanges.Should().Contain(range => range.RangeAddress.LastAddress.ColumnNumber == 3);
        sheet.PageSetup.PrintAreas.Single().RangeAddress.LastAddress.ColumnNumber.Should().Be(3);
    }

    [Fact]
    public async Task Pdf_export_is_landscape_and_repeats_headers_and_footer_on_every_page()
    {
        var path = await new PdfRecordExporter().ExportAsync(CreateReconciliationSnapshot(75), _root, CancellationToken.None);

        using var pdf = PdfDocument.Open(path);
        pdf.NumberOfPages.Should().BeGreaterThan(1);
        for (var index = 1; index <= pdf.NumberOfPages; index++)
        {
            var page = pdf.GetPage(index);
            var compactText = string.Concat(page.Text.Where(character => !char.IsWhiteSpace(character)));
            page.Width.Should().BeGreaterThan(page.Height);
            page.Width.Should().BeApproximately(841.89, 1);
            page.Height.Should().BeApproximately(595.28, 1);
            page.Text.Should().Contain("客户").And.Contain("规格");
            page.Text.Should().Contain("DZ-202608-0001").And.Contain("v3");
            compactText.Should().Contain("2026-08-0708:00").And.Contain("2026-08-0710:30");
            compactText.Should().Contain($"第{index}页/共{pdf.NumberOfPages}页");
        }

        string.Concat(pdf.GetPages().Select(page => page.Text))
            .Should().Contain("客户/工程对账单").And.Contain("32,220.34");
    }

    [Fact]
    public async Task Pdf_export_auto_paginates_long_multiline_content_and_places_tail_on_the_last_physical_page()
    {
        var path = await new PdfRecordExporter().ExportAsync(CreateLongContentSnapshot(), _root, CancellationToken.None);

        using var pdf = PdfDocument.Open(path);
        pdf.NumberOfPages.Should().BeGreaterThan(2);
        pdf.GetPages().Should().OnlyContain(page => !string.IsNullOrWhiteSpace(page.Text));
        pdf.GetPage(pdf.NumberOfPages).Text.Should().Contain("TAIL-MARKER").And.Contain("盖章区域");
        string.Concat(pdf.GetPages().Select(page => page.Text)).Should().Contain("第一行").And.Contain("第二行");
    }

    [Fact]
    public void Pdf_signature_and_stamp_row_is_at_least_twenty_millimetres_high()
    {
        PdfFontBootstrap.Initialize();
        var layout = new RecordLayoutBuilder().Build(CreateReconciliationSnapshot(3));

        var document = PdfRecordExporter.BuildDocument(layout);
        var signatureTable = document.Sections[0].Elements.Cast<DocumentObject>().OfType<Table>().Last();
        var row = signatureTable.Rows[0];

        row.HeightRule.Should().Be(RowHeightRule.AtLeast);
        row.Height.Point.Should().BeGreaterThanOrEqualTo(Unit.FromMillimeter(20).Point);
    }

    [Theory]
    [InlineData("客户<>:\"/\\|?*..对账单")]
    [InlineData("C:\\Windows\\..\\客户对账单")]
    [InlineData("../../客户对账单")]
    public void Archive_file_name_cannot_escape_destination_root(string templateName)
    {
        var root = Path.GetFullPath(_root);
        var snapshot = CreateReconciliationSnapshot(1, templateName);

        var path = ArchiveFileNamer.GetPath(snapshot, root, "xlsx");

        Path.GetFullPath(path).Should().StartWith(root + Path.DirectorySeparatorChar);
        Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Should().NotContain("..");
        Path.GetFileName(Path.GetDirectoryName(path)).Should().NotContainAny("<", ">", ":", "\"", "/", "\\", "|", "?", "*");
    }

    [Theory]
    [InlineData("CON", "PRN")]
    [InlineData("con.txt", "AUX.")]
    [InlineData("COM1 ", "lpt9.txt")]
    [InlineData("NUL...", "COM9")]
    public void Archive_file_name_sanitizes_windows_reserved_device_names(string templateName, string archiveNumber)
    {
        var path = ArchiveFileNamer.GetPath(CreateReconciliationSnapshot(1, templateName, archiveNumber), _root, "xlsx");
        var relativeSegments = Path.GetRelativePath(_root, path).Split(Path.DirectorySeparatorChar);

        relativeSegments[2].Should().StartWith("_");
        relativeSegments[3].Should().StartWith("_");
        Path.GetFullPath(path).Should().StartWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar);
    }

    [Fact]
    public async Task Pre_cancelled_export_leaves_no_temporary_or_partial_file()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => new ExcelRecordExporter().ExportAsync(CreateReconciliationSnapshot(1), _root, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public async Task Export_failure_is_wrapped_in_chinese_and_preserves_the_inner_exception()
    {
        var invalid = CreateReconciliationSnapshot(1, payloadJson: "{");

        var action = () => new PdfRecordExporter().ExportAsync(invalid, _root, CancellationToken.None);

        var error = await action.Should().ThrowAsync<OutputException>();
        error.Which.Message.Should().Contain("导出");
        error.Which.InnerException.Should().NotBeNull();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public void Output_snapshot_defensively_copies_nested_template_and_header_values()
    {
        var options = new List<string> { "AC-13" };
        var fields = new[] { new FieldDefinition("specification", "规格", FieldDataType.Select, Options: options) };
        var operands = new[] { "quantity", "unitPrice" };
        var formulas = new[] { new FormulaDefinition("amount", FormulaOperator.Multiply, operands) };
        var header = new Dictionary<string, string> { ["companyName"] = "原公司" };
        var snapshot = new OutputRecordSnapshot(
            "DZ-202608-0001", 1,
            new TemplateDefinition("id", "模板", "分类", 1, "DZ", fields, formulas, [], new PrintLayout("A4", "Portrait")),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), header, "{}", "首次封存",
            DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), DateTimeOffset.Parse("2026-08-07T10:30:00+08:00"));

        options[0] = "已篡改";
        fields[0] = new FieldDefinition("changed", "已篡改", FieldDataType.Text);
        operands[0] = "changed";
        header["companyName"] = "已篡改";

        snapshot.Template.Fields[0].Options.Should().Equal("AC-13");
        snapshot.Template.Fields[0].Key.Should().Be("specification");
        snapshot.Template.Formulas[0].Operands.Should().Equal("quantity", "unitPrice");
        snapshot.CompanyHeader["companyName"].Should().Be("原公司");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    internal static OutputRecordSnapshot CreateReconciliationSnapshot(
        int rowCount,
        string templateName = "客户/工程对账单",
        string archiveNumber = "DZ-202608-0001",
        string? payloadJson = null)
    {
        var template = new TemplateDefinition(
            "customer-reconciliation", templateName, "销售结算", 1, "DZ",
            [
                new("date", "日期", FieldDataType.Date, true),
                new("customer", "客户", FieldDataType.Text, true),
                new("projectPart", "工程部位", FieldDataType.Text, true),
                new("specification", "规格", FieldDataType.Select, true, Options: ["AC-13"]),
                new("vehicleCount", "车数", FieldDataType.Integer, true, IsSummable: true),
                new("quantity", "数量", FieldDataType.Decimal, true, IsSummable: true),
                new("unitPrice", "单价", FieldDataType.Decimal, true),
                new("materialAmountRaw", "材料金额原值", FieldDataType.Decimal, IsSummable: true),
                new("materialAmount", "材料金额", FieldDataType.Decimal, IsSummable: true),
                new("oilFee", "油费", FieldDataType.Decimal, true, IsSummable: true),
                new("freightFee", "运费", FieldDataType.Decimal, true, IsSummable: true),
                new("supplyTotal", "供货总额", FieldDataType.Decimal, IsSummable: true),
                new("payment", "回款", FieldDataType.Decimal, true, IsSummable: true),
                new("openingReceivable", "上期欠款", FieldDataType.Decimal, true),
                new("receivable", "累计欠款", FieldDataType.Decimal),
                new("reconciliationResult", "对账结果", FieldDataType.Select, true, Options: ["已确认"])
            ],
            [
                new("materialAmountRaw", FormulaOperator.Multiply, ["quantity", "unitPrice"]),
                new("materialAmount", FormulaOperator.Round, ["materialAmountRaw"]),
                new("supplyTotal", FormulaOperator.Add, ["materialAmount", "oilFee", "freightFee"]),
                new("receivable", FormulaOperator.Add, ["openingReceivable", "supplyTotal"]),
                new("receivable", FormulaOperator.Subtract, ["receivable", "payment"])
            ],
            ["date", "customer", "projectPart"],
            new PrintLayout("A4", "Landscape"));
        var rows = Enumerable.Range(0, rowCount).Select(index =>
            $$"""{"date":"2026-08-{{index % 28 + 1:00}}","customer":"华东路桥","projectPart":"东环线二标","specification":"AC-13","vehicleCount":3,"quantity":238.14,"unitPrice":135.30,"oilFee":0,"freightFee":0,"payment":10000,"openingReceivable":5000,"reconciliationResult":"已确认"}""");
        return new OutputRecordSnapshot(
            archiveNumber, 3, template,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            new Dictionary<string, string> { ["companyName"] = "华东沥青有限公司", ["preparedBy"] = "张伟", ["reviewedBy"] = "李敏" },
            payloadJson ?? $"{{\"rows\":[{string.Join(',', rows)}],\"remarks\":\"本期数据已经双方核对。\"}}",
            "修正运费", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), DateTimeOffset.Parse("2026-08-07T10:30:00+08:00"));
    }

    private static OutputRecordSnapshot CreateLongContentSnapshot()
    {
        var template = new TemplateDefinition(
            "long-content", "长文本分页测试表", "测试", 1, "LONG",
            [
                new("date", "日期", FieldDataType.Date, true),
                new("description", "说明", FieldDataType.Text, true),
                new("quantity", "数量", FieldDataType.Decimal, true, IsSummable: true)
            ],
            [], ["date"], new PrintLayout("A4", "Portrait"));
        var description = "第一行\n第二行 " + string.Concat(Enumerable.Repeat("很长的内容用于验证按实际高度分页。", 4));
        var rows = Enumerable.Range(1, 56).Select(index => new { date = $"2026-08-{index % 28 + 1:00}", description, quantity = index });
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            rows,
            remarks = string.Concat(Enumerable.Repeat("长备注用于验证尾部内容自动续页。", 180)) + " TAIL-MARKER"
        });
        return new OutputRecordSnapshot(
            "LONG-202608-0001", 2, template, new(2026, 8, 1), new(2026, 8, 31),
            new Dictionary<string, string> { ["companyName"] = "测试公司", ["preparedBy"] = "张伟" },
            payload, "分页验证", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), DateTimeOffset.Parse("2026-08-07T10:30:00+08:00"));
    }

    private static OutputRecordSnapshot CreateFirstColumnSummableSnapshot()
    {
        var template = new TemplateDefinition(
            "first-column-total", "首列合计测试表", "测试", 1, "SUM",
            [
                new("quantity", "数量", FieldDataType.Decimal, true, IsSummable: true),
                new("note", "说明", FieldDataType.Text)
            ],
            [], [], new PrintLayout("A4", "Portrait"));
        return new OutputRecordSnapshot(
            "SUM-202608-0001", 1, template, new(2026, 8, 1), new(2026, 8, 31),
            new Dictionary<string, string> { ["companyName"] = "测试公司" },
            """{"rows":[{"quantity":1,"note":"一"},{"quantity":2,"note":"二"},{"quantity":3,"note":"三"}]}""",
            "首次封存", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), DateTimeOffset.Parse("2026-08-07T10:30:00+08:00"));
    }

    private static OutputRecordSnapshot CreateAllColumnsSummableSnapshot()
    {
        var template = new TemplateDefinition(
            "all-columns-total", "全列合计测试表", "测试", 1, "ALLSUM",
            [
                new("quantity", "数量", FieldDataType.Decimal, true, IsSummable: true),
                new("amount", "金额", FieldDataType.Decimal, true, IsSummable: true)
            ],
            [], [], new PrintLayout("A4", "Portrait"));
        return new OutputRecordSnapshot(
            "ALLSUM-202608-0001", 1, template, new(2026, 8, 1), new(2026, 8, 31),
            new Dictionary<string, string> { ["companyName"] = "测试公司" },
            """{"rows":[{"quantity":1,"amount":10},{"quantity":2,"amount":20},{"quantity":3,"amount":30}]}""",
            "首次封存", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), DateTimeOffset.Parse("2026-08-07T10:30:00+08:00"));
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var reader = new StreamReader(archive.GetEntry(path)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
