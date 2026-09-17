using System.Globalization;
using System.Text.Json;
using AsphaltPlantManager.Core.MasterData;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Core.Templates;
using ClosedXML.Excel;

namespace AsphaltPlantManager.Infrastructure.Output;

public sealed class ExcelFormWorkflow : IExcelFormWorkflow
{
    private const int HeaderRow = 5;
    private const int FirstDataRow = 6;
    private const int DraftRowCount = 30;
    private readonly ITemplateCatalog _templates;
    private readonly IMasterDataRepository? _masterData;
    private readonly TemplateEngine _engine = new();

    public ExcelFormWorkflow(ITemplateCatalog templates, IMasterDataRepository? masterData = null)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _masterData = masterData;
    }

    public async Task<ExcelFormDraft> CreateDraftAsync(
        TemplateDefinition template,
        string companyName,
        DateOnly periodStart,
        DateOnly periodEnd,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDirectory);
        var path = Path.Combine(destinationDirectory, $"{Sanitize(template.Name)}-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}.xlsx");

        using var workbook = BuildWorkbook(template, companyName ?? string.Empty, periodStart, periodEnd, commonData: await LoadCommonDataAsync(cancellationToken).ConfigureAwait(false));
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        workbook.SaveAs(stream, true, true);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return new ExcelFormDraft(path, template.Id, template.Version);
    }

    public async Task<ExcelFormDraft> CreateEditableDraftAsync(
        OutputRecordSnapshot snapshot,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDirectory);
        var path = Path.Combine(destinationDirectory, $"{Sanitize(snapshot.ArchiveNumber)}-v{snapshot.Version}-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}.xlsx");
        var template = ToTemplateDefinition(snapshot.Template);
        var rows = ParseRows(snapshot.PayloadJson);
        var company = snapshot.CompanyHeader.TryGetValue("company", out var companyName) ? companyName : string.Empty;
        using var workbook = BuildWorkbook(template, company, snapshot.PeriodStart, snapshot.PeriodEnd, rows,
            new ArchiveMetadata(snapshot.RecordId?.ToString("D") ?? string.Empty, snapshot.ArchiveNumber, snapshot.Version),
            await LoadCommonDataAsync(cancellationToken).ConfigureAwait(false));
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        workbook.SaveAs(stream, true, true);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return new ExcelFormDraft(path, snapshot.Template.Id, snapshot.Template.Version);
    }

    public async Task<ImportedExcelForm> ImportAsync(string workbookPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(workbookPath)) throw Invalid("找不到 Excel 文件。", null);

        try
        {
            using var workbook = new XLWorkbook(workbookPath);
            var metadata = workbook.Worksheets.FirstOrDefault(sheet => sheet.Name == "__FormMeta")
                ?? throw Invalid("Excel 缺少表单元数据。", null);
            var templateId = metadata.Cell("B1").GetString();
            var templateVersion = metadata.Cell("B2").GetValue<int>();
            var template = await _templates.GetAsync(templateId, templateVersion, cancellationToken).ConfigureAwait(false);
            var sheet = workbook.Worksheets.FirstOrDefault(item => item.Name == template.Name)
                ?? throw Invalid("Excel 缺少表格工作表。", null);
            var firstDataRow = metadata.Cell("B7").GetValue<int>();
            var periodStart = ReadDate(sheet.Cell(3, 2), metadata.Cell("B3").GetString());
            var periodEnd = ReadDate(sheet.Cell(3, 4), metadata.Cell("B4").GetString());
            var recordId = TryReadGuid(metadata.Cell("B8").GetString());
            var archiveNumber = NullIfBlank(metadata.Cell("B9").GetString());
            var archiveVersion = metadata.Cell("B10").TryGetValue<int>(out var parsedVersion) ? parsedVersion : (int?)null;
            var formulaTargets = template.Formulas.Select(formula => formula.Target).ToHashSet(StringComparer.Ordinal);
            var fieldColumns = template.Fields.Select((field, index) => (field.Key, Column: index + 1))
                .ToDictionary(item => item.Key, item => item.Column, StringComparer.Ordinal);
            var rows = new List<Dictionary<string, object?>>();
            var lastRow = Math.Max(firstDataRow, sheet.LastRowUsed()?.RowNumber() ?? firstDataRow);

            for (var rowNumber = firstDataRow; rowNumber <= lastRow; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hasInput = template.Fields
                    .Where(field => !formulaTargets.Contains(field.Key))
                    .Any(field => !sheet.Cell(rowNumber, fieldColumns[field.Key]).IsEmpty());
                if (!hasInput) continue;

                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var column = 0; column < template.Fields.Count; column++)
                {
                    var field = template.Fields[column];
                    if (formulaTargets.Contains(field.Key)) continue;
                    values[field.Key] = ReadCell(sheet.Cell(rowNumber, column + 1), field.DataType);
                }

                try
                {
                    foreach (var pair in _engine.Calculate(template, values)) values[pair.Key] = pair.Value;
                    var errors = _engine.Validate(template, values);
                    if (errors.Count > 0) throw Invalid(string.Join("；", errors.Select(error => error.Message)), null);
                }
                catch (OutputException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw Invalid($"第 {rowNumber} 行数据无效。", exception);
                }

                rows.Add(values);
            }

            if (rows.Count == 0) throw Invalid("Excel 中没有可导入的明细行。", null);
            var header = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["company"] = metadata.Cell("B5").GetString()
            };
            foreach (var key in new[] { "customer", "project", "projectPart", "specification" })
            {
                if (rows[0].TryGetValue(key, out var value) && value is not null)
                    header[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            var payload = JsonSerializer.Serialize(new { rows }, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            var search = string.Join(' ', rows.SelectMany(row => row.Values.Where(value => value is not null).Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))));
            return new ImportedExcelForm(template.Id, template.Version, periodStart, periodEnd, header, payload, search, recordId, archiveNumber, archiveVersion);
        }
        catch (OutputException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Invalid("读取 Excel 失败，请确认文件是本软件生成的模板。", exception);
        }
    }

    private static XLWorkbook BuildWorkbook(
        TemplateDefinition template,
        string companyName,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? seedRows = null,
        ArchiveMetadata? archive = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? commonData = null)
    {
        var workbook = new XLWorkbook { CalculateMode = XLCalculateMode.Auto };
        var metadata = workbook.Worksheets.Add("__FormMeta");
        metadata.Cell("A1").Value = "TemplateId"; metadata.Cell("B1").Value = template.Id;
        metadata.Cell("A2").Value = "TemplateVersion"; metadata.Cell("B2").Value = template.Version;
        metadata.Cell("A3").Value = "PeriodStart"; metadata.Cell("B3").Value = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        metadata.Cell("A4").Value = "PeriodEnd"; metadata.Cell("B4").Value = periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        metadata.Cell("A5").Value = "CompanyName"; metadata.Cell("B5").Value = companyName;
        metadata.Cell("A6").Value = "HeaderRow"; metadata.Cell("B6").Value = HeaderRow;
        metadata.Cell("A7").Value = "FirstDataRow"; metadata.Cell("B7").Value = FirstDataRow;
        metadata.Cell("A8").Value = "RecordId"; metadata.Cell("B8").Value = archive?.RecordId ?? string.Empty;
        metadata.Cell("A9").Value = "ArchiveNumber"; metadata.Cell("B9").Value = archive?.ArchiveNumber ?? string.Empty;
        metadata.Cell("A10").Value = "ArchiveVersion"; metadata.Cell("B10").Value = archive?.ArchiveVersion.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        metadata.Visibility = XLWorksheetVisibility.VeryHidden;

        var commonDataSheet = commonData is not null && commonData.Values.Any(values => values.Count > 0)
            ? workbook.Worksheets.Add("__CommonData")
            : null;
        commonDataSheet?.Hide();
        if (commonDataSheet is not null && commonData is not null)
        {
            var commonDataColumns = new Dictionary<string, string>(StringComparer.Ordinal);
            var commonColumn = 1;
            foreach (var pair in commonData.Where(pair => pair.Value.Count > 0))
            {
                commonDataSheet.Cell(1, commonColumn).Value = pair.Key;
                for (var row = 0; row < pair.Value.Count; row++) commonDataSheet.Cell(row + 2, commonColumn).Value = pair.Value[row];
                commonDataColumns[pair.Key] = GetColumnLetter(commonColumn);
                commonColumn++;
            }
            commonDataSheet.Visibility = XLWorksheetVisibility.VeryHidden;
        }

        var sheet = workbook.Worksheets.Add(SanitizeWorksheetName(template.Name));
        var columnCount = Math.Max(1, template.Fields.Count);
        sheet.Range(1, 1, 1, columnCount).Merge().Value = template.Name;
        sheet.Range(2, 1, 2, columnCount).Merge().Value = companyName;
        sheet.Cell(3, 1).Value = "期间开始"; sheet.Cell(3, 2).Value = periodStart.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(3, 3).Value = "期间结束"; sheet.Cell(3, 4).Value = periodEnd.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(3, 2).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Cell(3, 4).Style.DateFormat.Format = "yyyy-mm-dd";
        for (var column = 0; column < template.Fields.Count; column++)
            sheet.Cell(HeaderRow, column + 1).Value = template.Fields[column].Label;

        if (commonDataSheet is not null)
        {
            var commonDataColumns = commonDataSheet.Row(1).CellsUsed().ToDictionary(cell => cell.GetString(), cell => GetColumnLetter(cell.Address.ColumnNumber), StringComparer.Ordinal);
            ApplyCommonDataValidations(template, sheet, commonDataSheet, commonDataColumns);
        }

        var fieldColumns = template.Fields.Select((field, index) => (field.Key, Column: index + 1))
            .ToDictionary(item => item.Key, item => item.Column, StringComparer.Ordinal);
        var dataRowCount = Math.Max(DraftRowCount, seedRows?.Count ?? 0);
        for (var rowIndex = 0; rowIndex < dataRowCount; rowIndex++)
        {
            var row = FirstDataRow + rowIndex;
            if (seedRows is not null && rowIndex < seedRows.Count)
            {
                foreach (var field in template.Fields.Where(field => !template.Formulas.Any(formula => formula.Target == field.Key)))
                {
                    if (seedRows[rowIndex].TryGetValue(field.Key, out var value) && value is not null)
                        SetTypedValue(sheet.Cell(row, fieldColumns[field.Key]), value, field.DataType);
                }
            }
            foreach (var formula in template.Formulas)
            {
                var target = template.Fields.Select((field, index) => (field.Key, Column: index + 1))
                    .Single(item => string.Equals(item.Key, formula.Target, StringComparison.Ordinal)).Column;
                sheet.Cell(row, target).FormulaA1 = BuildFormula(template, formula.Target, row);
            }
        }

        var lastRow = FirstDataRow + dataRowCount - 1;
        var used = sheet.Range(HeaderRow, 1, lastRow, columnCount);
        used.SetAutoFilter();
        sheet.SheetView.FreezeRows(HeaderRow);
        sheet.ShowGridLines = false;
        sheet.Range(1, 1, 1, columnCount).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, columnCount).Style.Font.FontSize = 18;
        sheet.Range(1, 1, 3, columnCount).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Range(HeaderRow, 1, HeaderRow, columnCount).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
        sheet.Range(HeaderRow, 1, HeaderRow, columnCount).Style.Font.FontColor = XLColor.White;
        sheet.Range(HeaderRow, 1, HeaderRow, columnCount).Style.Font.Bold = true;
        sheet.Range(HeaderRow, 1, lastRow, columnCount).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sheet.Range(HeaderRow, 1, lastRow, columnCount).Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        sheet.Columns(1, columnCount).AdjustToContents(1, Math.Min(lastRow, HeaderRow + 5));
        foreach (var column in sheet.Columns(1, columnCount)) column.Width = Math.Clamp(column.Width + 2, 12, 28);
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.PageOrientation = template.PrintLayout.Orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase)
            ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.PagesTall = 0;
        sheet.PageSetup.SetRowsToRepeatAtTop(HeaderRow, HeaderRow);
        sheet.PageSetup.PrintAreas.Add(1, 1, lastRow, columnCount);
        return workbook;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCommonDataAsync(CancellationToken cancellationToken)
    {
        if (_masterData is null) return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pair in CommonDataCategories)
        {
            var values = (await _masterData.ListAsync(pair.Value, cancellationToken).ConfigureAwait(false)).Select(item => item.Key).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (values.Length > 0) result[pair.Key] = values;
        }
        return result;
    }

    private static void ApplyCommonDataValidations(
        TemplateDefinition template,
        IXLWorksheet? sheet,
        IXLWorksheet commonDataSheet,
        IReadOnlyDictionary<string, string> commonDataColumns)
    {
        if (sheet is null) return;
        foreach (var (field, fieldColumn) in template.Fields.Select((field, index) => (field, index + 1)))
        {
            if (!CommonDataCategories.ContainsKey(field.Key) || !commonDataColumns.TryGetValue(field.Key, out var listColumn)) continue;
            var listCount = commonDataSheet.Column(ClosedXmlColumnNumber(listColumn)).CellsUsed().Count() - 1;
            if (listCount <= 0) continue;
            var validation = sheet.Range(FirstDataRow, fieldColumn, FirstDataRow + DraftRowCount + 200, fieldColumn).CreateDataValidation();
            validation.IgnoreBlanks = true;
            validation.InCellDropdown = true;
            validation.List($"'__CommonData'!${listColumn}$2:${listColumn}${listCount + 1}", true);
        }
    }

    private static int ClosedXmlColumnNumber(string column)
    {
        var number = 0;
        foreach (var character in column) number = number * 26 + character - 'A' + 1;
        return number;
    }

    private static object? ReadCell(IXLCell cell, FieldDataType dataType)
    {
        if (cell.IsEmpty()) return null;
        return dataType switch
        {
            FieldDataType.Date => DateOnly.FromDateTime(cell.GetDateTime()),
            FieldDataType.Decimal => cell.GetValue<decimal>(),
            FieldDataType.Integer => cell.GetValue<long>(),
            _ => cell.GetString().Trim()
        };
    }

    private static void SetTypedValue(IXLCell cell, object value, FieldDataType dataType)
    {
        switch (dataType)
        {
            case FieldDataType.Date:
                var date = value switch
                {
                    DateOnly dateOnly => dateOnly,
                    DateTime dateTime => DateOnly.FromDateTime(dateTime),
                    _ when DateOnly.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
                    _ => throw new InvalidDataException("日期值无效。")
                };
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "yyyy-mm-dd";
                break;
            case FieldDataType.Decimal:
                cell.Value = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                cell.Style.NumberFormat.Format = "#,##0.00";
                break;
            case FieldDataType.Integer:
                cell.Value = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                cell.Style.NumberFormat.Format = "#,##0";
                break;
            default:
                cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                break;
        }
    }

    private static DateOnly ReadDate(IXLCell cell, string fallback)
    {
        if (!cell.IsEmpty() && cell.TryGetValue<DateTime>(out var date)) return DateOnly.FromDateTime(date);
        if (DateOnly.TryParseExact(fallback, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return parsed;
        throw Invalid("Excel 的期间日期无效。", null);
    }

    private static string BuildFormula(TemplateDefinition template, string target, int row)
    {
        var columns = template.Fields.Select((field, index) => (field.Key, Column: GetColumnLetter(index + 1)))
            .ToDictionary(item => item.Key, item => item.Column, StringComparer.Ordinal);
        var expressions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var formula in template.Formulas)
        {
            var operands = formula.Operands.Select(operand =>
                expressions.TryGetValue(operand, out var expression)
                    ? $"({expression})"
                    : $"{columns[operand]}{row}").ToArray();
            expressions[formula.Target] = formula.Operator switch
            {
                FormulaOperator.Add or FormulaOperator.Sum => string.Join("+", operands),
                FormulaOperator.Subtract => string.Join("-", operands),
                FormulaOperator.Multiply => string.Join("*", operands),
                FormulaOperator.Round => $"ROUND({operands.Single()},{formula.DecimalPlaces})",
                _ => throw new InvalidOperationException($"不支持公式运算符“{formula.Operator}”。")
            };
        }

        return expressions[target];
    }

    private static string GetColumnLetter(int column)
    {
        var result = string.Empty;
        while (column > 0) { column--; result = (char)('A' + column % 26) + result; column /= 26; }
        return result;
    }

    private static string Sanitize(string value) => new(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
    private static string SanitizeWorksheetName(string value) => Sanitize(value).Replace(':', '_')[..Math.Min(31, value.Length)];
    private static OutputException Invalid(string message, Exception? inner) => new(message, inner ?? new InvalidDataException(message));

    private static Guid? TryReadGuid(string value) => Guid.TryParse(value, out var result) ? result : null;
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ParseRows(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var elements = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray()
            : root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array
                ? rows.EnumerateArray()
                : throw new JsonException("输出数据缺少 rows 数组。");
        return elements.Select(element => element.EnumerateObject().ToDictionary(property => property.Name, property => ConvertJsonValue(property.Value), StringComparer.Ordinal)).ToArray();
    }

    private static object? ConvertJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };

    private static TemplateDefinition ToTemplateDefinition(OutputTemplateSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.Name,
        snapshot.Category,
        snapshot.Version,
        snapshot.ArchivePrefix,
        snapshot.Fields.Select(field => new FieldDefinition(field.Key, field.Label, field.DataType, field.Required, field.DefaultValue, field.Options, field.IsSearchable, field.IsSummable)).ToArray(),
        snapshot.Formulas.Select(formula => new FormulaDefinition(formula.Target, formula.Operator, formula.Operands.ToArray(), formula.DecimalPlaces)).ToArray(),
        snapshot.QueryFields.ToArray(),
        new PrintLayout(snapshot.PrintLayout.Paper, snapshot.PrintLayout.Orientation));

    private sealed record ArchiveMetadata(string RecordId, string ArchiveNumber, int ArchiveVersion);

    private static readonly IReadOnlyDictionary<string, string> CommonDataCategories = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["customer"] = "客户",
        ["supplier"] = "供应商",
        ["vehiclePlate"] = "车辆",
        ["vehicle"] = "车辆",
        ["driver"] = "司机"
    };
}
