using System.Text.Json;
using System.Text.Json.Serialization;
using AsphaltPlantManager.Core.Templates;

namespace AsphaltPlantManager.Infrastructure.Templates;

public sealed class JsonTemplateCatalog : ITemplateCatalog
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _directory;

    public JsonTemplateCatalog(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public async Task<TemplateDefinition> GetAsync(
        string id,
        int? version,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var templates = await ListAsync(cancellationToken).ConfigureAwait(false);
        var matchingTemplates = templates.Where(template => string.Equals(template.Id, id, StringComparison.Ordinal));
        var result = version is { } requestedVersion
            ? matchingTemplates.SingleOrDefault(template => template.Version == requestedVersion)
            : matchingTemplates.OrderByDescending(template => template.Version).FirstOrDefault();

        return result ?? throw new KeyNotFoundException(
            version is { }
                ? $"未找到模板“{id}”的版本 {version}。"
                : $"未找到模板“{id}”。");
    }

    public async Task<IReadOnlyList<TemplateDefinition>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(_directory, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var loadedTemplates = new List<(TemplateDefinition Template, string Path)>();

        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            loadedTemplates.Add((await LoadTemplateAsync(path, cancellationToken).ConfigureAwait(false), path));
        }

        foreach (var duplicate in loadedTemplates
                     .GroupBy(item => (item.Template.Id, item.Template.Version))
                     .Where(group => group.Count() > 1))
        {
            var paths = string.Join("；", duplicate.Select(item => item.Path));
            throw new InvalidDataException(
                $"模板“{duplicate.Key.Id}”版本 {duplicate.Key.Version} 重复定义：{paths}。");
        }

        return loadedTemplates
            .Select(item => item.Template)
            .OrderBy(template => template.Id, StringComparer.Ordinal)
            .ThenBy(template => template.Version)
            .ToArray();
    }

    private static async Task<TemplateDefinition> LoadTemplateAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            ValidateRequiredEnumProperties(document.RootElement, path);
            var template = document.RootElement.Deserialize<TemplateDefinition>(SerializerOptions)
                ?? throw new JsonException("模板内容为空。");
            ValidateTemplate(template, path);
            return template;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"模板文件“{path}”不是有效 JSON：{exception.Message}", exception);
        }
        catch (InvalidDataException)
        {
            throw;
        }
    }

    private static void ValidateTemplate(TemplateDefinition template, string path)
    {
        Require(template.Id, "id", path);
        Require(template.Name, "name", path);
        Require(template.Category, "category", path);
        Require(template.ArchivePrefix, "archivePrefix", path);
        if (template.Version <= 0)
        {
            throw InvalidTemplate(path, "version 必须为正整数");
        }

        if (template.Fields is null || template.Formulas is null || template.QueryFields is null || template.PrintLayout is null)
        {
            throw InvalidTemplate(path, "缺少 fields、formulas、searchFields 或 printLayout");
        }

        Require(template.PrintLayout.Paper, "printLayout.paper", path);
        Require(template.PrintLayout.Orientation, "printLayout.orientation", path);

        foreach (var field in template.Fields)
        {
            Require(field.Key, "fields[].key", path);
            Require(field.Label, "fields[].label", path);
            if (!Enum.IsDefined(field.DataType))
            {
                throw InvalidTemplate(path, $"字段“{field.Key}”的 dataType 无效");
            }

            if (field.DataType == FieldDataType.Select && field.Options is not { Count: > 0 })
            {
                throw InvalidTemplate(path, $"字段“{field.Key}”缺少下拉选项");
            }
        }

        foreach (var formula in template.Formulas)
        {
            Require(formula.Target, "formulas[].target", path);
            if (!Enum.IsDefined(formula.Operator))
            {
                throw InvalidTemplate(path, $"公式“{formula.Target}”的 operator 无效");
            }

            if (formula.Operands is not { Length: > 0 })
            {
                throw InvalidTemplate(path, $"公式“{formula.Target}”缺少操作数");
            }

            if (formula.Operands.Any(string.IsNullOrWhiteSpace))
            {
                throw InvalidTemplate(path, $"公式“{formula.Target}”包含空白操作数");
            }

            if (formula.DecimalPlaces is < 0 or > 6)
            {
                throw InvalidTemplate(path, $"公式“{formula.Target}”的 decimalPlaces 必须在 0 到 6 之间");
            }

            if (formula.Operator == FormulaOperator.Round && formula.Operands.Length != 1)
            {
                throw InvalidTemplate(path, $"公式“{formula.Target}”的 Round 运算只能有一个操作数");
            }
        }
    }

    private static void ValidateRequiredEnumProperties(JsonElement root, string path)
    {
        if (TryGetProperty(root, "fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                if (!TryGetProperty(field, "dataType", out _))
                {
                    throw InvalidTemplate(path, "字段缺少关键字段 dataType");
                }
            }
        }

        if (TryGetProperty(root, "formulas", out var formulas) && formulas.ValueKind == JsonValueKind.Array)
        {
            foreach (var formula in formulas.EnumerateArray())
            {
                if (!TryGetProperty(formula, "operator", out _))
                {
                    throw InvalidTemplate(path, "公式缺少关键字段 operator");
                }
            }
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static void Require(string? value, string field, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw InvalidTemplate(path, $"缺少关键字段 {field}");
        }
    }

    private static InvalidDataException InvalidTemplate(string path, string detail) =>
        new($"模板文件“{path}”无效：{detail}。");
}
