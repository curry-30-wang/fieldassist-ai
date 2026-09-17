using AsphaltPlantManager.Core.Receivables;
using ClosedXML.Excel;
using System.Globalization;

namespace AsphaltPlantManager.Infrastructure.Output;

public sealed class CustomerReceivableExcelExporter : ICustomerReceivableExporter
{
    public async Task<string> ExportAsync(CustomerReceivable receivable, string destinationDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receivable);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDirectory);
        var safeName = new string(receivable.CustomerName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
        var path = Path.Combine(destinationDirectory, $"客户欠款-{safeName}-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("客户欠款");
        sheet.Cell("A1").Value = "客户欠款对账单";
        sheet.Range("A1:G1").Merge();
        sheet.Cell("A2").Value = "客户"; sheet.Cell("B2").Value = receivable.CustomerName;
        sheet.Cell("A3").Value = "应收金额"; sheet.Cell("B3").Value = (double)receivable.ReceivableAmount;
        sheet.Cell("C3").Value = "已收金额"; sheet.Cell("D3").Value = (double)receivable.PaidAmount;
        sheet.Cell("E3").Value = "剩余欠款"; sheet.Cell("F3").Value = (double)receivable.RemainingAmount;
        sheet.Cell("A4").Value = "收款日期"; sheet.Cell("B4").Value = receivable.DueDate?.ToDateTime(TimeOnly.MinValue);
        sheet.Cell("C4").Value = "状态"; sheet.Cell("D4").Value = receivable.StatusText;
        sheet.Cell("A5").Value = "备注"; sheet.Range("B5:G5").Merge(); sheet.Cell("B5").Value = receivable.Note;
        sheet.Range("A1:G1").Style.Font.Bold = true;
        sheet.Range("A1:G1").Style.Font.FontSize = 18;
        sheet.Range("A2:G5").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sheet.Range("A2:G5").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sheet.Range("A2:G5").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Range("B3:B3").Style.NumberFormat.Format = "#,##0.00";
        sheet.Range("D3:D3").Style.NumberFormat.Format = "#,##0.00";
        sheet.Range("F3:F3").Style.NumberFormat.Format = "#,##0.00";
        sheet.Cell("B4").Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Columns().AdjustToContents();
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        workbook.SaveAs(stream, true, true);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return path;
    }
}
