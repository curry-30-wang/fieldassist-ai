using AsphaltPlantManager.Core.Templates;
using FluentAssertions;
using Xunit;

namespace AsphaltPlantManager.Core.Tests.Templates;

public sealed class TemplateEngineTests
{
    [Fact]
    public void Inventory_balance_equals_opening_plus_purchase_minus_consumption()
    {
        var template = new TemplateDefinition(
            "asphalt-inventory",
            "沥青购耗存明细表",
            "库存",
            1,
            "GHC",
            [],
            [
                new FormulaDefinition("closing", FormulaOperator.Add, ["opening", "purchase"]),
                new FormulaDefinition("closing", FormulaOperator.Subtract, ["closing", "consumption"])
            ],
            [],
            new PrintLayout("A4", "Landscape"));
        var data = new Dictionary<string, object?>
        {
            ["opening"] = 10m,
            ["purchase"] = 30m,
            ["consumption"] = 12.5m
        };

        var result = new TemplateEngine().Calculate(template, data);

        result["closing"].Should().Be(27.5m);
    }

    [Fact]
    public void Calculation_rounds_half_away_from_zero_to_the_default_two_decimal_places()
    {
        var round = Enum.Parse<FormulaOperator>("Round");
        var template = CreateFormulaTemplate(new FormulaDefinition("materialAmount", round, ["rawAmount"]));

        var result = new TemplateEngine().Calculate(
            template,
            new Dictionary<string, object?> { ["rawAmount"] = 32220.345m });

        result["materialAmount"].Should().Be(32220.35m);
    }

    [Fact]
    public void Calculation_rejects_round_with_negative_decimal_places()
    {
        var template = CreateFormulaTemplate(new FormulaDefinition("materialAmount", FormulaOperator.Round, ["rawAmount"], -1));

        Action calculate = () => new TemplateEngine().Calculate(
            template,
            new Dictionary<string, object?> { ["rawAmount"] = 1m });

        calculate.Should().Throw<InvalidOperationException>()
            .WithMessage("公式“materialAmount”的 decimalPlaces 必须在 0 到 6 之间。");
    }

    [Fact]
    public void Calculation_copies_input_without_adding_formula_results_to_the_source()
    {
        var template = CreateFormulaTemplate(new FormulaDefinition("closing", FormulaOperator.Sum, ["opening", "purchase"]));
        var input = new Dictionary<string, object?> { ["opening"] = 10m, ["purchase"] = 30m };

        var result = new TemplateEngine().Calculate(template, input);

        input.Should().NotContainKey("closing");
        result["closing"].Should().Be(40m);
    }

    [Fact]
    public void Calculation_rejects_a_missing_formula_operand_instead_of_treating_it_as_zero()
    {
        var template = CreateFormulaTemplate(new FormulaDefinition("closing", FormulaOperator.Sum, ["opening", "purchase"]));

        Action calculate = () => new TemplateEngine().Calculate(
            template,
            new Dictionary<string, object?> { ["opening"] = 10m });

        calculate.Should().Throw<InvalidOperationException>()
            .WithMessage("公式操作数“purchase”缺失。");
    }

    [Fact]
    public void Calculation_rejects_a_non_numeric_formula_operand()
    {
        var template = CreateFormulaTemplate(new FormulaDefinition("closing", FormulaOperator.Sum, ["opening", "purchase"]));

        Action calculate = () => new TemplateEngine().Calculate(
            template,
            new Dictionary<string, object?> { ["opening"] = 10m, ["purchase"] = "invalid" });

        calculate.Should().Throw<InvalidOperationException>()
            .WithMessage("公式操作数“purchase”不是有效数字。");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Calculation_rejects_a_null_or_blank_programmatic_formula_operand(string? operand)
    {
        var template = CreateFormulaTemplate(new FormulaDefinition("closing", FormulaOperator.Sum, [operand!]));

        Action calculate = () => new TemplateEngine().Calculate(
            template,
            new Dictionary<string, object?> { ["opening"] = 10m });

        calculate.Should().Throw<InvalidOperationException>()
            .WithMessage("公式“closing”包含空白操作数。");
    }

    [Fact]
    public void Validation_returns_the_field_key_and_Chinese_message_for_blank_required_value()
    {
        var errors = new TemplateEngine().Validate(
            CreateTemplate(new FieldDefinition("supplier", "供应商", FieldDataType.Text, Required: true)),
            new Dictionary<string, object?> { ["supplier"] = "   " });

        errors.Should().ContainSingle()
            .Which.Should().Be(new ValidationError("supplier", "供应商为必填项。"));
    }

    [Fact]
    public void Validation_rejects_a_non_decimal_value()
    {
        var errors = new TemplateEngine().Validate(
            CreateTemplate(new FieldDefinition("quantity", "数量", FieldDataType.Decimal)),
            new Dictionary<string, object?> { ["quantity"] = "twelve" });

        errors.Should().ContainSingle()
            .Which.Should().Be(new ValidationError("quantity", "数量必须是有效数字。"));
    }

    [Fact]
    public void Validation_rejects_an_invalid_date()
    {
        var errors = new TemplateEngine().Validate(
            CreateTemplate(new FieldDefinition("date", "日期", FieldDataType.Date)),
            new Dictionary<string, object?> { ["date"] = "2026-02-30" });

        errors.Should().ContainSingle()
            .Which.Should().Be(new ValidationError("date", "日期必须是有效日期。"));
    }

    [Fact]
    public void Validation_rejects_a_non_integer_value()
    {
        var errors = new TemplateEngine().Validate(
            CreateTemplate(new FieldDefinition("trucks", "车数", FieldDataType.Integer)),
            new Dictionary<string, object?> { ["trucks"] = 3.5m });

        errors.Should().ContainSingle()
            .Which.Should().Be(new ValidationError("trucks", "车数必须是有效整数。"));
    }

    [Fact]
    public void Validation_rejects_a_value_not_in_the_select_options()
    {
        var errors = new TemplateEngine().Validate(
            CreateTemplate(new FieldDefinition("grade", "规格", FieldDataType.Select, Options: ["AC-13", "AC-16"])),
            new Dictionary<string, object?> { ["grade"] = "AC-20" });

        errors.Should().ContainSingle()
            .Which.Should().Be(new ValidationError("grade", "规格必须从下拉选项中选择。"));
    }

    private static TemplateDefinition CreateTemplate(FieldDefinition field) => new(
        "test-template",
        "测试模板",
        "测试",
        1,
        "TEST",
        [field],
        [],
        [],
        new PrintLayout("A4", "Portrait"));

    private static TemplateDefinition CreateFormulaTemplate(FormulaDefinition formula) => new(
        "test-template",
        "测试模板",
        "测试",
        1,
        "TEST",
        [],
        [formula],
        [],
        new PrintLayout("A4", "Portrait"));
}
