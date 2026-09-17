using System.Globalization;
using System.Text.Json;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Core.Templates;

namespace AsphaltPlantManager.Infrastructure.Output;

public sealed class RecordLayoutBuilder
{
    private const double A4WidthPoints = 595.28;
    private const double A4HeightPoints = 841.89;
    private const double MarginPoints = 36;

    public PrintPreviewDocument Build(OutputRecordSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var orientation = ParseOrientation(snapshot.Template.PrintLayout);
        var rows = ParseRows(snapshot);
        var pageSize = orientation == OutputOrientation.Landscape
            ? (Width: A4HeightPoints, Height: A4WidthPoints)
            : (Width: A4WidthPoints, Height: A4HeightPoints);

        return new PrintPreviewDocument(
            pageSize.Width,
            pageSize.Height,
            MarginPoints,
            orientation,
            snapshot.Template.Name,
            GetHeader(snapshot.CompanyHeader, "companyName", "company", "公司名称"),
            $"期间：{snapshot.PeriodStart:yyyy-MM-dd} 至 {snapshot.PeriodEnd:yyyy-MM-dd}",
            $"档案号：{snapshot.ArchiveNumber}    版本：v{snapshot.Version}    版本时间：{snapshot.VersionedAt:yyyy-MM-dd HH:mm}    制表时间：{snapshot.PrintedAt:yyyy-MM-dd HH:mm}",
            snapshot.Template.Fields.Select(field => field.Label).ToArray(),
            rows,
            BuildTotals(snapshot.Template, rows),
            ParseRemarks(snapshot.PayloadJson, snapshot.ChangeNote),
            [
                $"制表：{GetHeader(snapshot.CompanyHeader, "preparedBy", "制表人")}",
                $"审核：{GetHeader(snapshot.CompanyHeader, "reviewedBy", "审核人")}",
                "签字：_______________"
            ],
            "盖章区域",
            $"档案号：{snapshot.ArchiveNumber}    版本：v{snapshot.Version}    版本时间：{snapshot.VersionedAt:yyyy-MM-dd HH:mm}    打印时间：{snapshot.PrintedAt:yyyy-MM-dd HH:mm}");
    }

    private static IReadOnlyList<IReadOnlyList<PrintCellValue>> ParseRows(OutputRecordSnapshot snapshot)
    {
        using var document = JsonDocument.Parse(snapshot.PayloadJson);
        var rows = new List<IReadOnlyList<PrintCellValue>>();
        foreach (var rowElement in GetRowElements(document.RootElement))
        {
            var values = rowElement.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ConvertJsonValue(property.Value),
                StringComparer.Ordinal);
            ApplyFormulas(snapshot.Template, values);
            rows.Add(snapshot.Template.Fields.Select(field => CreateCell(field, values.GetValueOrDefault(field.Key))).ToArray());
        }

        return rows.AsReadOnly();
    }

    private static void ApplyFormulas(OutputTemplateSnapshot template, IDictionary<string, object?> values)
    {
        foreach (var formula in template.Formulas)
        {
            var operands = formula.Operands.Select(operand => GetDecimal(operand, values)).ToArray();
            values[formula.Target] = formula.Operator switch
            {
                FormulaOperator.Add or FormulaOperator.Sum => operands.Aggregate(0m, (total, value) => total + value),
                FormulaOperator.Subtract => operands.Skip(1).Aggregate(operands[0], (total, value) => total - value),
                FormulaOperator.Multiply => operands.Aggregate(1m, (total, value) => total * value),
                FormulaOperator.Round when operands.Length == 1 => decimal.Round(operands[0], formula.DecimalPlaces, MidpointRounding.AwayFromZero),
                FormulaOperator.Round => throw new InvalidOperationException($"公式“{formula.Target}”的 Round 运算只能有一个操作数。"),
                _ => throw new InvalidOperationException($"公式“{formula.Target}”使用了不支持的运算符。")
            };
        }
    }

    private static decimal GetDecimal(string operand, IDictionary<string, object?> values)
    {
        if (!values.TryGetValue(operand, out var value) || value is null)
        {
            throw new InvalidOperationException($"公式操作数“{operand}”缺失。");
        }

        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException($"公式操作数“{operand}”不是有效数字。", exception);
        }
    }

    private static IEnumerable<JsonElement> GetRowElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Select(RequireObject).ToArray();
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("输出数据必须是对象或对象数组。");
        }

        if (root.TryGetProperty("rows", out var rows))
        {
            if (rows.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("输出数据的 rows 必须是数组。");
            }

            return rows.EnumerateArray().Select(RequireObject).ToArray();
        }

        return [root];
    }

    private static JsonElement RequireObject(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object ? value : throw new JsonException("明细行必须是对象。");

    private static object? ConvertJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };

    private static PrintCellValue CreateCell(OutputFieldSnapshot field, object? value)
    {
        if (value is null)
        {
            return new PrintCellValue(null, OutputValueKind.Text, string.Empty);
        }

        return field.DataType switch
        {
            FieldDataType.Date => CreateDateCell(value),
            FieldDataType.Decimal => CreateNumberCell(value, OutputValueKind.Number, "#,##0.00"),
            FieldDataType.Integer => CreateNumberCell(value, OutputValueKind.Integer, "0"),
            _ => new PrintCellValue(value.ToString(), OutputValueKind.Text, value.ToString() ?? string.Empty)
        };
    }

    private static PrintCellValue CreateDateCell(object value)
    {
        var date = value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            string text when DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
            _ => throw new InvalidOperationException($"日期值“{value}”无效。")
        };
        return new PrintCellValue(date, OutputValueKind.Date, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static PrintCellValue CreateNumberCell(object value, OutputValueKind kind, string format)
    {
        var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        return new PrintCellValue(number, kind, number.ToString(format, CultureInfo.InvariantCulture));
    }

    private static IReadOnlyList<string> BuildTotals(
        OutputTemplateSnapshot template,
        IReadOnlyList<IReadOnlyList<PrintCellValue>> rows)
    {
        var totals = new List<string>();
        for (var column = 0; column < template.Fields.Count; column++)
        {
            var field = template.Fields[column];
            if (!field.IsSummable)
            {
                continue;
            }

            var total = rows.Sum(row => row[column].Value is null ? 0m : Convert.ToDecimal(row[column].Value, CultureInfo.InvariantCulture));
            totals.Add($"{field.Label}：{total:#,##0.00}");
        }

        return totals.AsReadOnly();
    }

    private static string ParseRemarks(string payloadJson, string fallback)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
               document.RootElement.TryGetProperty("remarks", out var remarks) &&
               remarks.ValueKind == JsonValueKind.String
            ? remarks.GetString() ?? fallback
            : fallback;
    }

    private static OutputOrientation ParseOrientation(OutputPrintLayoutSnapshot printLayout)
    {
        if (!string.Equals(printLayout.Paper, "A4", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"暂不支持纸张规格“{printLayout.Paper}”。");
        }

        return printLayout.Orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase)
            ? OutputOrientation.Landscape
            : printLayout.Orientation.Equals("Portrait", StringComparison.OrdinalIgnoreCase)
                ? OutputOrientation.Portrait
                : throw new InvalidOperationException($"不支持打印方向“{printLayout.Orientation}”。");
    }

    private static string GetHeader(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
