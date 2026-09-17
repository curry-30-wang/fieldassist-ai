using System.Globalization;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Core.Templates;
using ClosedXML.Excel;

namespace AsphaltPlantManager.Infrastructure.Output;

public sealed class ExcelRecordExporter : IRecordExporter
{
    public string Format => "xlsx";

    public async Task<string> ExportAsync(
        OutputRecordSnapshot snapshot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var layout = new RecordLayoutBuilder().Build(snapshot);
            var path = ArchiveFileNamer.GetPath(snapshot, destinationRoot, Format);
            await AtomicOutputFile.WriteAsync(
                path,
                (stream, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    using var workbook = BuildWorkbook(snapshot, layout);
                    workbook.SaveAs(stream, true, true);
                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);
            return path;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OutputException("Excel 导出失败，请检查目标目录后重试。", exception);
        }
    }

    private static XLWorkbook BuildWorkbook(OutputRecordSnapshot snapshot, PrintPreviewDocument layout)
    {
        var workbook = new XLWorkbook { CalculateMode = XLCalculateMode.Auto };
        var worksheet = workbook.Worksheets.Add(SanitizeWorksheetName(snapshot.Template.Name));
        var columnCount = snapshot.Template.Fields.Count;
        var allColumnsSummable = columnCount > 0 && snapshot.Template.Fields.All(field => field.IsSummable);
        var lastColumn = Math.Max(1, columnCount + (allColumnsSummable ? 1 : 0));
        var headerRow = 5;
        var firstDataRow = headerRow + 1;
        var rows = layout.Rows.ToArray();
        var lastDataRow = firstDataRow + rows.Length - 1;
        var totalRow = Math.Max(firstDataRow, lastDataRow + 1);

        worksheet.Range(1, 1, 1, lastColumn).Merge().Value = snapshot.Template.Name;
        worksheet.Range(2, 1, 2, lastColumn).Merge().Value = layout.CompanyName;
        worksheet.Range(3, 1, 3, lastColumn).Merge().Value =
            $"{layout.Period}    档案号：{snapshot.ArchiveNumber}    版本：v{snapshot.Version}";
        worksheet.Range(4, 1, 4, lastColumn).Merge().Value =
            $"版本时间：{snapshot.VersionedAt:yyyy-MM-dd HH:mm}    制表时间：{snapshot.PrintedAt:yyyy-MM-dd HH:mm}    变更说明：{snapshot.ChangeNote}";

        for (var column = 0; column < columnCount; column++)
        {
            worksheet.Cell(headerRow, column + 1).Value = snapshot.Template.Fields[column].Label;
        }

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var excelRow = firstDataRow + rowIndex;
            for (var column = 0; column < columnCount; column++)
            {
                var field = snapshot.Template.Fields[column];
                var cell = worksheet.Cell(excelRow, column + 1);
                if (snapshot.Template.Formulas.Any(formula => formula.Target == field.Key))
                {
                    cell.FormulaA1 = BuildFormula(snapshot.Template, field.Key, excelRow);
                    ApplyNumberFormat(cell, field.DataType);
                    continue;
                }

                SetTypedValue(cell, rows[rowIndex][column], field.DataType);
            }
        }

        var totalLabelColumn = allColumnsSummable
            ? columnCount
            : snapshot.Template.Fields
                .Select((field, index) => (field, index))
                .First(item => !item.field.IsSummable)
                .index;
        if (columnCount > 0)
        {
            worksheet.Cell(totalRow, totalLabelColumn + 1).Value = "合计";
        }

        for (var column = 0; column < columnCount; column++)
        {
            var field = snapshot.Template.Fields[column];
            if (field.IsSummable && rows.Length > 0)
            {
                var letter = worksheet.Column(column + 1).ColumnLetter();
                worksheet.Cell(totalRow, column + 1).FormulaA1 = $"SUM({letter}{firstDataRow}:{letter}{lastDataRow})";
                ApplyNumberFormat(worksheet.Cell(totalRow, column + 1), field.DataType);
            }
        }

        var notesRow = totalRow + 1;
        worksheet.Range(notesRow, 1, notesRow, lastColumn).Merge().Value = $"备注：{layout.Remarks}";
        var signaturesRow = notesRow + 2;
        worksheet.Range(signaturesRow, 1, signaturesRow, lastColumn).Merge().Value =
            string.Join("        ", layout.SignatureLabels) + $"        {layout.StampLabel}";

        StyleWorksheet(worksheet, lastColumn, headerRow, firstDataRow, totalRow, notesRow, signaturesRow);
        ConfigurePrinting(worksheet, layout, headerRow, signaturesRow, lastColumn, snapshot);
        workbook.RecalculateAllFormulas();
        return workbook;
    }

    private static void StyleWorksheet(
        IXLWorksheet worksheet,
        int lastColumn,
        int headerRow,
        int firstDataRow,
        int totalRow,
        int notesRow,
        int signaturesRow)
    {
        worksheet.ShowGridLines = false;
        worksheet.SheetView.FreezeRows(headerRow);
        worksheet.Range(1, 1, signaturesRow, lastColumn).Style.Font.FontName = "Microsoft YaHei";
        worksheet.Range(1, 1, 1, lastColumn).Style.Font.Bold = true;
        worksheet.Range(1, 1, 1, lastColumn).Style.Font.FontSize = 18;
        worksheet.Range(1, 1, 1, lastColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Range(2, 1, 2, lastColumn).Style.Font.Bold = true;
        worksheet.Range(2, 1, 2, lastColumn).Style.Font.FontSize = 12;
        worksheet.Range(2, 1, 2, lastColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Range(3, 1, 4, lastColumn).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
        var headers = worksheet.Range(headerRow, 1, headerRow, lastColumn);
        headers.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
        headers.Style.Font.SetBold().Font.FontColor = XLColor.White;
        headers.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headers.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headers.Style.Alignment.WrapText = true;
        if (totalRow >= firstDataRow)
        {
            var data = worksheet.Range(firstDataRow, 1, totalRow, lastColumn);
            data.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            data.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            data.Style.Border.OutsideBorderColor = XLColor.FromHtml("#6B7280");
            data.Style.Border.InsideBorderColor = XLColor.FromHtml("#D1D5DB");
        }

        worksheet.Range(totalRow, 1, totalRow, lastColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
        worksheet.Range(totalRow, 1, totalRow, lastColumn).Style.Font.SetBold();
        worksheet.Range(notesRow, 1, notesRow, lastColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
        worksheet.Range(notesRow, 1, notesRow, lastColumn).Style.Alignment.SetWrapText();
        worksheet.Range(signaturesRow, 1, signaturesRow, lastColumn).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        worksheet.Rows(1, 2).Height = 25;
        worksheet.Row(headerRow).Height = 34;
        worksheet.Row(notesRow).Height = 28;
        worksheet.Columns(1, lastColumn).AdjustToContents(1, Math.Min(signaturesRow, 50));
        foreach (var column in worksheet.Columns(1, lastColumn))
        {
            column.Width = Math.Clamp(column.Width + 1.5, 10, 22);
        }
    }

    private static void ConfigurePrinting(
        IXLWorksheet worksheet,
        PrintPreviewDocument layout,
        int headerRow,
        int lastRow,
        int lastColumn,
        OutputRecordSnapshot snapshot)
    {
        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        worksheet.PageSetup.PageOrientation = layout.Orientation == OutputOrientation.Landscape
            ? XLPageOrientation.Landscape
            : XLPageOrientation.Portrait;
        worksheet.PageSetup.PagesWide = 1;
        worksheet.PageSetup.PagesTall = 0;
        worksheet.PageSetup.Margins.Top = 0.45;
        worksheet.PageSetup.Margins.Bottom = 0.55;
        worksheet.PageSetup.Margins.Left = 0.35;
        worksheet.PageSetup.Margins.Right = 0.35;
        worksheet.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);
        worksheet.PageSetup.PrintAreas.Add(1, 1, lastRow, lastColumn);
        worksheet.PageSetup.Footer.Center.AddText("第 &P 页 / 共 &N 页");
        worksheet.PageSetup.Footer.Left.AddText($"档案号：{snapshot.ArchiveNumber}  v{snapshot.Version}");
        worksheet.PageSetup.Footer.Right.AddText($"版本时间：{snapshot.VersionedAt:yyyy-MM-dd HH:mm}  打印时间：{snapshot.PrintedAt:yyyy-MM-dd HH:mm}");
    }

    private static void SetTypedValue(IXLCell cell, PrintCellValue value, FieldDataType dataType)
    {
        if (value.Value is null)
        {
            cell.Value = Blank.Value;
            return;
        }

        switch (dataType)
        {
            case FieldDataType.Date:
                var date = (DateOnly)value.Value;
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "yyyy-mm-dd";
                break;
            case FieldDataType.Decimal:
                cell.Value = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
                ApplyNumberFormat(cell, dataType);
                break;
            case FieldDataType.Integer:
                cell.Value = Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
                ApplyNumberFormat(cell, dataType);
                break;
            default:
                cell.Value = value.DisplayText;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                break;
        }
    }

    private static void ApplyNumberFormat(IXLCell cell, FieldDataType dataType) =>
        cell.Style.NumberFormat.Format = dataType == FieldDataType.Integer ? "#,##0" : "#,##0.00";

    private static string BuildFormula(OutputTemplateSnapshot template, string target, int row)
    {
        var columnByKey = template.Fields
            .Select((field, index) => (field.Key, Column: GetColumnLetter(index + 1)))
            .ToDictionary(item => item.Key, item => item.Column, StringComparer.Ordinal);
        var expressions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var formula in template.Formulas)
        {
            var operands = formula.Operands.Select(operand =>
                operand == formula.Target && expressions.TryGetValue(operand, out var prior)
                    ? $"({prior})"
                    : $"{columnByKey[operand]}{row}").ToArray();
            expressions[formula.Target] = formula.Operator switch
            {
                FormulaOperator.Add or FormulaOperator.Sum => string.Join("+", operands),
                FormulaOperator.Subtract => string.Join("-", operands),
                FormulaOperator.Multiply => string.Join("*", operands),
                FormulaOperator.Round => $"ROUND({operands.Single()},{formula.DecimalPlaces})",
                _ => throw new InvalidOperationException($"不支持公式运算符“{formula.Operator}”。")
            };
        }

        return expressions.TryGetValue(target, out var expression)
            ? expression
            : throw new InvalidOperationException($"字段“{target}”没有可导出的公式。");
    }

    private static string GetColumnLetter(int column)
    {
        var result = string.Empty;
        while (column > 0)
        {
            column--;
            result = (char)('A' + column % 26) + result;
            column /= 26;
        }

        return result;
    }

    private static string SanitizeWorksheetName(string name)
    {
        var invalid = new HashSet<char>([':', '\\', '/', '?', '*', '[', ']']);
        var clean = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? "导出表" : clean[..Math.Min(clean.Length, 31)];
    }
}
