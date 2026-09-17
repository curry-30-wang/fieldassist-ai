using System.Globalization;
using System.Text.Json;

namespace AsphaltPlantManager.Core.Templates;

public sealed class TemplateEngine
{
    public IReadOnlyDictionary<string, object?> Calculate(
        TemplateDefinition template,
        IReadOnlyDictionary<string, object?> input)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(input);

        var output = new Dictionary<string, object?>(input, StringComparer.Ordinal);
        foreach (var formula in template.Formulas)
        {
            output[formula.Target] = EvaluateDecimal(formula, output);
        }

        return output;
    }

    public IReadOnlyList<ValidationError> Validate(
        TemplateDefinition template,
        IReadOnlyDictionary<string, object?> input)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(input);

        var errors = new List<ValidationError>();
        foreach (var field in template.Fields)
        {
            input.TryGetValue(field.Key, out var value);
            if (IsEmpty(value))
            {
                if (field.Required)
                {
                    errors.Add(new ValidationError(field.Key, $"{field.Label}为必填项。"));
                }

                continue;
            }

            var message = field.DataType switch
            {
                FieldDataType.Date when !IsDate(value) => $"{field.Label}必须是有效日期。",
                FieldDataType.Decimal when !TryGetDecimal(value, out _) => $"{field.Label}必须是有效数字。",
                FieldDataType.Integer when !IsInteger(value) => $"{field.Label}必须是有效整数。",
                FieldDataType.Select when !IsOption(value, field.Options) => $"{field.Label}必须从下拉选项中选择。",
                _ => null
            };

            if (message is not null)
            {
                errors.Add(new ValidationError(field.Key, message));
            }
        }

        return errors;
    }

    private static decimal EvaluateDecimal(
        FormulaDefinition formula,
        IReadOnlyDictionary<string, object?> values)
    {
        if (formula.Operands is not { Length: > 0 })
        {
            throw new InvalidOperationException($"公式“{formula.Target}”至少需要一个操作数。");
        }

        if (formula.Operands.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"公式“{formula.Target}”包含空白操作数。");
        }

        if (formula.DecimalPlaces is < 0 or > 6)
        {
            throw new InvalidOperationException($"公式“{formula.Target}”的 decimalPlaces 必须在 0 到 6 之间。");
        }

        var operands = formula.Operands.Select(operand => GetDecimal(operand, values)).ToArray();
        return formula.Operator switch
        {
            FormulaOperator.Add or FormulaOperator.Sum => operands.Aggregate(0m, (total, value) => total + value),
            FormulaOperator.Subtract => operands.Skip(1).Aggregate(operands[0], (total, value) => total - value),
            FormulaOperator.Multiply => operands.Aggregate(1m, (total, value) => total * value),
            FormulaOperator.Round when operands.Length == 1 => decimal.Round(operands[0], formula.DecimalPlaces, MidpointRounding.AwayFromZero),
            FormulaOperator.Round => throw new InvalidOperationException($"公式“{formula.Target}”的 Round 运算只能有一个操作数。"),
            _ => throw new InvalidOperationException($"公式“{formula.Target}”使用了不支持的运算符。")
        };
    }

    private static decimal GetDecimal(string operand, IReadOnlyDictionary<string, object?> values)
    {
        if (!values.TryGetValue(operand, out var value))
        {
            throw new InvalidOperationException($"公式操作数“{operand}”缺失。");
        }

        if (TryGetDecimal(value, out var result))
        {
            return result;
        }

        throw new InvalidOperationException($"公式操作数“{operand}”不是有效数字。");
    }

    private static bool IsEmpty(object? value) => value is null or "" || value is string text && string.IsNullOrWhiteSpace(text);

    private static bool IsDate(object? value) => value switch
    {
        DateOnly or DateTime or DateTimeOffset => true,
        JsonElement { ValueKind: JsonValueKind.String } element =>
            DateOnly.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        string text => DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        _ => false
    };

    private static bool IsInteger(object? value) =>
        TryGetDecimal(value, out var decimalValue) && decimal.Truncate(decimalValue) == decimalValue;

    private static bool IsOption(object? value, IReadOnlyList<string>? options)
    {
        var option = value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };

        return option is not null && options?.Contains(option, StringComparer.Ordinal) == true;
    }

    private static bool TryGetDecimal(object? value, out decimal result)
    {
        try
        {
            switch (value)
            {
                case decimal decimalValue:
                    result = decimalValue;
                    return true;
                case byte byteValue:
                    result = byteValue;
                    return true;
                case sbyte sbyteValue:
                    result = sbyteValue;
                    return true;
                case short shortValue:
                    result = shortValue;
                    return true;
                case ushort ushortValue:
                    result = ushortValue;
                    return true;
                case int intValue:
                    result = intValue;
                    return true;
                case uint uintValue:
                    result = uintValue;
                    return true;
                case long longValue:
                    result = longValue;
                    return true;
                case ulong ulongValue:
                    result = ulongValue;
                    return true;
                case float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue):
                    result = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                    return true;
                case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                    result = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                    return true;
                case string text:
                    return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
                case JsonElement { ValueKind: JsonValueKind.Number } element:
                    return element.TryGetDecimal(out result);
                case JsonElement { ValueKind: JsonValueKind.String } element:
                    return decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out result);
                default:
                    result = default;
                    return false;
            }
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }
}
