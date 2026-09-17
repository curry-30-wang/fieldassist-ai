using AsphaltPlantManager.Core.Templates;
using AsphaltPlantManager.Infrastructure.Templates;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Templates;

public sealed class JsonTemplateCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"AsphaltPlantManager-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetAsync_returns_the_requested_historic_version_and_the_latest_version_when_unspecified()
    {
        WriteTemplate("asphalt-inventory", 1, "沥青购耗存明细表 v1");
        WriteTemplate("asphalt-inventory", 2, "沥青购耗存明细表 v2");
        var catalog = new JsonTemplateCatalog(_directory);

        var historic = await catalog.GetAsync("asphalt-inventory", 1, default);
        var latest = await catalog.GetAsync("asphalt-inventory", null, default);

        Assert.Equal(1, historic.Version);
        Assert.Equal("沥青购耗存明细表 v1", historic.Name);
        Assert.Equal(2, latest.Version);
        Assert.Equal("沥青购耗存明细表 v2", latest.Name);
    }

    [Fact]
    public async Task ListAsync_rejects_duplicate_template_id_and_version_with_the_source_paths()
    {
        var firstPath = WriteTemplate("asphalt-inventory", 1, "第一个");
        var secondPath = WriteTemplate("asphalt-inventory", 1, "第二个", "copy.json");
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(firstPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains(secondPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_rejects_invalid_json_with_its_source_path()
    {
        var path = WriteFile("invalid.json", "{ invalid json");
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_rejects_a_template_missing_a_required_field_with_its_source_path()
    {
        var path = WriteFile(
            "missing-name.json",
            """
            {
              "id": "asphalt-inventory",
              "category": "库存",
              "version": 1,
              "archivePrefix": "GHC",
              "fields": [],
              "formulas": [],
              "searchFields": [],
              "printLayout": { "paper": "A4", "orientation": "Landscape" }
            }
            """);
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_rejects_a_template_missing_search_fields_with_its_source_path()
    {
        var path = WriteFile(
            "missing-search-fields.json",
            """
            {
              "id": "asphalt-inventory", "name": "测试", "category": "库存", "version": 1, "archivePrefix": "GHC",
              "fields": [], "formulas": [], "printLayout": { "paper": "A4", "orientation": "Landscape" }
            }
            """);
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("searchFields", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_rejects_a_field_missing_data_type_with_its_source_path()
    {
        var path = WriteFile(
            "missing-data-type.json",
            """
            {
              "id": "asphalt-inventory", "name": "测试", "category": "库存", "version": 1, "archivePrefix": "GHC",
              "fields": [{ "key": "quantity", "label": "数量" }], "formulas": [], "searchFields": [],
              "printLayout": { "paper": "A4", "orientation": "Landscape" }
            }
            """);
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("缺少", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dataType", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_rejects_a_formula_missing_operator_with_its_source_path()
    {
        var path = WriteFile(
            "missing-operator.json",
            """
            {
              "id": "asphalt-inventory", "name": "测试", "category": "库存", "version": 1, "archivePrefix": "GHC",
              "fields": [], "formulas": [{ "target": "closing", "operands": ["opening"] }], "searchFields": [],
              "printLayout": { "paper": "A4", "orientation": "Landscape" }
            }
            """);
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("缺少", exception.Message, StringComparison.Ordinal);
        Assert.Contains("operator", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dataType", "99")]
    [InlineData("operator", "99")]
    public async Task ListAsync_rejects_an_out_of_range_integer_enum_with_its_source_path(string property, string value)
    {
        var isField = property == "dataType";
        var path = WriteFile(
            $"invalid-{property}.json",
            isField
                ? $$"""
                    {
                      "id": "asphalt-inventory", "name": "测试", "category": "库存", "version": 1, "archivePrefix": "GHC",
                      "fields": [{ "key": "quantity", "label": "数量", "dataType": {{value}} }], "formulas": [], "searchFields": [],
                      "printLayout": { "paper": "A4", "orientation": "Landscape" }
                    }
                    """
                : $$"""
                    {
                      "id": "asphalt-inventory", "name": "测试", "category": "库存", "version": 1, "archivePrefix": "GHC",
                      "fields": [], "formulas": [{ "target": "closing", "operator": {{value}}, "operands": ["opening"] }], "searchFields": [],
                      "printLayout": { "paper": "A4", "orientation": "Landscape" }
                    }
                    """
        );
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("无效", exception.Message, StringComparison.Ordinal);
        Assert.Contains(property, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"  \"")]
    public async Task ListAsync_rejects_a_null_or_blank_formula_operand_with_its_source_path(string operand)
    {
        var path = WriteFile(
            "invalid-operand.json",
            $$"""
            {
              "id": "asphalt-inventory", "name": "测试", "category": "库存", "version": 1, "archivePrefix": "GHC",
              "fields": [], "formulas": [{ "target": "closing", "operator": "sum", "operands": [{{operand}}] }], "searchFields": [],
              "printLayout": { "paper": "A4", "orientation": "Landscape" }
            }
            """);
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("空白操作数", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_rejects_round_formulas_with_an_invalid_decimal_place_count()
    {
        var path = WriteFile(
            "invalid-round-decimals.json",
            """
            {
              "id": "asphalt-inventory", "name": "测试", "category": "库存", "version": 1, "archivePrefix": "GHC",
              "fields": [{ "key": "amount", "label": "金额", "dataType": "decimal" }],
              "formulas": [{ "target": "roundedAmount", "operator": "round", "operands": ["amount"], "decimalPlaces": 7 }],
              "searchFields": [], "printLayout": { "paper": "A4", "orientation": "Landscape" }
            }
            """);
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("decimalPlaces", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_rejects_round_formulas_with_negative_decimal_places_and_the_source_path()
    {
        var path = WriteFile(
            "negative-round-decimals.json",
            """
            {
              "id": "asphalt-inventory", "name": "测试", "category": "库存", "version": 1, "archivePrefix": "GHC",
              "fields": [{ "key": "amount", "label": "金额", "dataType": "decimal" }],
              "formulas": [{ "target": "roundedAmount", "operator": "round", "operands": ["amount"], "decimalPlaces": -1 }],
              "searchFields": [], "printLayout": { "paper": "A4", "orientation": "Landscape" }
            }
            """);
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("decimalPlaces", exception.Message, StringComparison.Ordinal);
        Assert.Contains("必须在 0 到 6 之间", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAsync_rejects_round_formulas_with_multiple_operands()
    {
        var path = WriteFile(
            "invalid-round-operands.json",
            """
            {
              "id": "asphalt-inventory", "name": "测试", "category": "库存", "version": 1, "archivePrefix": "GHC",
              "fields": [{ "key": "amount", "label": "金额", "dataType": "decimal" }],
              "formulas": [{ "target": "roundedAmount", "operator": "round", "operands": ["amount", "amount"] }],
              "searchFields": [], "printLayout": { "paper": "A4", "orientation": "Landscape" }
            }
            """);
        var catalog = new JsonTemplateCatalog(_directory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => catalog.ListAsync(default));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("只能有一个操作数", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Loaded_round_formula_uses_its_configured_decimal_places()
    {
        WriteFile(
            "round-decimals.json",
            """
            {
              "id": "asphalt-inventory", "name": "测试", "category": "库存", "version": 1, "archivePrefix": "GHC",
              "fields": [{ "key": "amount", "label": "金额", "dataType": "decimal" }, { "key": "roundedAmount", "label": "舍入金额", "dataType": "decimal" }],
              "formulas": [{ "target": "roundedAmount", "operator": "round", "operands": ["amount"], "decimalPlaces": 3 }],
              "searchFields": [], "printLayout": { "paper": "A4", "orientation": "Landscape" }
            }
            """);
        var template = await new JsonTemplateCatalog(_directory).GetAsync("asphalt-inventory", 1, default);

        var result = new TemplateEngine().Calculate(template, new Dictionary<string, object?> { ["amount"] = 1.2345m });

        Assert.Equal(1.235m, result["roundedAmount"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteTemplate(string id, int version, string name, string? fileName = null) => WriteFile(
        fileName ?? $"{id}-v{version}.json",
        $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "category": "库存",
          "version": {{version}},
          "archivePrefix": "GHC",
          "fields": [],
          "formulas": [],
          "searchFields": [],
          "printLayout": { "paper": "A4", "orientation": "Landscape" }
        }
        """);

    private string WriteFile(string fileName, string content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
