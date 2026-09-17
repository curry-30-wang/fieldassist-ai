using System.Collections;
using System.Text.Json;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Core.Templates;
using FluentAssertions;
using Xunit;

namespace AsphaltPlantManager.Core.Tests.Output;

public sealed class OutputRecordSnapshotTests
{
    [Fact]
    public void Snapshot_uses_immutable_projection_and_is_isolated_in_both_directions()
    {
        var sourceJson = JsonDocument.Parse("{\"threshold\":12.5}");
        var options = new List<string> { "AC-13" };
        var fields = new List<FieldDefinition>
        {
            new("specification", "规格", FieldDataType.Select, DefaultValue: sourceJson.RootElement, Options: options)
        };
        var operands = new[] { "quantity", "unitPrice" };
        var formulas = new List<FormulaDefinition> { new("amount", FormulaOperator.Multiply, operands) };
        var queryFields = new List<string> { "specification" };
        var source = new TemplateDefinition(
            "id", "模板", "分类", 1, "DZ", fields, formulas, queryFields, new PrintLayout("A4", "Portrait"));

        var snapshot = CreateSnapshot(source);
        sourceJson.Dispose();

        snapshot.Template.Should().BeOfType<OutputTemplateSnapshot>();
        snapshot.Template.Fields.Should().BeAssignableTo<IReadOnlyList<OutputFieldSnapshot>>();
        snapshot.Template.Formulas.Should().BeAssignableTo<IReadOnlyList<OutputFormulaSnapshot>>();
        snapshot.Template.Fields.Should().NotBeAssignableTo<Array>();
        snapshot.Template.Formulas.Should().NotBeAssignableTo<Array>();
        snapshot.Template.QueryFields.Should().NotBeAssignableTo<Array>();
        snapshot.Template.Fields[0].Options.Should().NotBeAssignableTo<Array>();
        snapshot.Template.Formulas[0].Operands.Should().NotBeAssignableTo<Array>();

        options[0] = "已篡改";
        fields[0] = new FieldDefinition("changed", "已篡改", FieldDataType.Text);
        operands[0] = "changed";
        queryFields[0] = "changed";

        snapshot.Template.Fields[0].Key.Should().Be("specification");
        snapshot.Template.Fields[0].Options.Should().Equal("AC-13");
        snapshot.Template.Formulas[0].Operands.Should().Equal("quantity", "unitPrice");
        snapshot.Template.QueryFields.Should().Equal("specification");
        ((JsonElement)snapshot.Template.Fields[0].DefaultValue!).GetProperty("threshold").GetDecimal().Should().Be(12.5m);

        var mutateFields = () => ((IList)snapshot.Template.Fields)[0] = snapshot.Template.Fields[0];
        var mutateOptions = () => ((IList)snapshot.Template.Fields[0].Options!)[0] = "changed";
        var mutateOperands = () => ((IList)snapshot.Template.Formulas[0].Operands)[0] = "changed";
        var mutateQueries = () => ((IList)snapshot.Template.QueryFields)[0] = "changed";

        mutateFields.Should().Throw<NotSupportedException>();
        mutateOptions.Should().Throw<NotSupportedException>();
        mutateOperands.Should().Throw<NotSupportedException>();
        mutateQueries.Should().Throw<NotSupportedException>();
        source.Fields[0].Key.Should().Be("changed");
    }

    private static OutputRecordSnapshot CreateSnapshot(TemplateDefinition template) => new(
        "DZ-202608-0001", 1, template,
        new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
        new Dictionary<string, string> { ["companyName"] = "测试公司" },
        "{}", "首次封存",
        DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"),
        DateTimeOffset.Parse("2026-08-07T10:30:00+08:00"));
}
