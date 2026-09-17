using AsphaltPlantManager.Core.Receivables;
using AsphaltPlantManager.Infrastructure.Output;
using ClosedXML.Excel;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Output;

public sealed class CustomerReceivableExcelExporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"AsphaltPlantManager-ReceivableExport-{Guid.NewGuid():N}");

    [Fact]
    public async Task Exports_a_readable_customer_receivable_workbook()
    {
        var exporter = new CustomerReceivableExcelExporter();
        var receivable = new CustomerReceivable(Guid.NewGuid(), "甲客户", 1234.5m, 200m, new DateOnly(2026, 8, 31), "月底前收款", DateTimeOffset.UtcNow);

        var path = await exporter.ExportAsync(receivable, _directory, CancellationToken.None);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.Single();
        Assert.Equal("客户欠款对账单", sheet.Cell("A1").GetString());
        Assert.Equal("甲客户", sheet.Cell("B2").GetString());
        Assert.Equal(1034.5, sheet.Cell("F3").GetValue<double>(), 2);
        Assert.Equal("部分收款", sheet.Cell("D4").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
