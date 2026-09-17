using AsphaltPlantManager.Core.Templates;
using AsphaltPlantManager.Core.Output;
using AsphaltPlantManager.Infrastructure.Output;
using AsphaltPlantManager.Infrastructure.Templates;
using Xunit;

namespace AsphaltPlantManager.Infrastructure.Tests.Templates;

public sealed class BuiltInTemplateTests
{
    private static readonly string[] ExpectedTemplateIds =
    [
        "asphalt-inventory",
        "customer-reconciliation",
        "production-daily",
        "material-receipt",
        "inventory-count",
        "finished-goods-dispatch",
        "sales-collection",
        "equipment-maintenance",
        "fuel-inventory",
        "quality-inspection",
        "transport-settlement",
        "safety-inspection"
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, FieldDataType>> RequiredFields =
        new Dictionary<string, IReadOnlyDictionary<string, FieldDataType>>
        {
            ["asphalt-inventory"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["opening"] = FieldDataType.Decimal,
                ["purchase"] = FieldDataType.Decimal,
                ["specification"] = FieldDataType.Text,
                ["ratio"] = FieldDataType.Decimal,
                ["production"] = FieldDataType.Decimal,
                ["consumption"] = FieldDataType.Decimal,
                ["closing"] = FieldDataType.Decimal
            },
            ["customer-reconciliation"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["customer"] = FieldDataType.Text,
                ["projectPart"] = FieldDataType.Text,
                ["specification"] = FieldDataType.Text,
                ["vehicleCount"] = FieldDataType.Integer,
                ["quantity"] = FieldDataType.Decimal,
                ["unitPrice"] = FieldDataType.Decimal,
                ["materialAmount"] = FieldDataType.Decimal,
                ["oilFee"] = FieldDataType.Decimal,
                ["freightFee"] = FieldDataType.Decimal,
                ["supplyTotal"] = FieldDataType.Decimal,
                ["payment"] = FieldDataType.Decimal,
                ["openingReceivable"] = FieldDataType.Decimal,
                ["receivable"] = FieldDataType.Decimal,
                ["reconciliationResult"] = FieldDataType.Select
            },
            ["production-daily"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["shift"] = FieldDataType.Select,
                ["mixRatio"] = FieldDataType.Decimal,
                ["ac13Production"] = FieldDataType.Decimal,
                ["ac16Production"] = FieldDataType.Decimal,
                ["ac20Production"] = FieldDataType.Decimal,
                ["sma13Production"] = FieldDataType.Decimal,
                ["downtimeReason"] = FieldDataType.Text
            },
            ["material-receipt"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["vehiclePlate"] = FieldDataType.Text,
                ["grossWeight"] = FieldDataType.Decimal,
                ["tareWeight"] = FieldDataType.Decimal,
                ["netWeight"] = FieldDataType.Decimal
            },
            ["inventory-count"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["profitLossAmount"] = FieldDataType.Decimal,
                ["handlingOpinion"] = FieldDataType.Text
            },
            ["finished-goods-dispatch"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["driver"] = FieldDataType.Text,
                ["weighbridgeWeight"] = FieldDataType.Decimal,
                ["dispatchTime"] = FieldDataType.Text,
                ["receivingStatus"] = FieldDataType.Select
            },
            ["sales-collection"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["contractAmount"] = FieldDataType.Decimal,
                ["cumulativeSupply"] = FieldDataType.Decimal,
                ["receivedPayment"] = FieldDataType.Decimal,
                ["unpaidPayment"] = FieldDataType.Decimal,
                ["accountAge"] = FieldDataType.Integer,
                ["agreedPaymentDate"] = FieldDataType.Date
            },
            ["equipment-maintenance"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["maintenanceContent"] = FieldDataType.Text,
                ["partsCost"] = FieldDataType.Decimal,
                ["nextMaintenanceDate"] = FieldDataType.Date
            },
            ["fuel-inventory"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["opening"] = FieldDataType.Decimal,
                ["dieselPurchase"] = FieldDataType.Decimal,
                ["issueTarget"] = FieldDataType.Text,
                ["issueQuantity"] = FieldDataType.Decimal,
                ["unitPrice"] = FieldDataType.Decimal,
                ["amount"] = FieldDataType.Decimal,
                ["inventory"] = FieldDataType.Decimal
            },
            ["quality-inspection"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["sampleBatch"] = FieldDataType.Text,
                ["testItem"] = FieldDataType.Text,
                ["standardValue"] = FieldDataType.Text,
                ["measuredValue"] = FieldDataType.Decimal,
                ["conclusion"] = FieldDataType.Select,
                ["inspector"] = FieldDataType.Text
            },
            ["transport-settlement"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["tripCount"] = FieldDataType.Integer
            },
            ["safety-inspection"] = new Dictionary<string, FieldDataType>
            {
                ["date"] = FieldDataType.Date,
                ["responsiblePerson"] = FieldDataType.Text,
                ["reviewResult"] = FieldDataType.Select
            }
        };

    private static readonly IReadOnlyDictionary<string, string[]> SummableFields =
        new Dictionary<string, string[]>
        {
            ["asphalt-inventory"] = ["purchase", "production", "consumption"],
            ["customer-reconciliation"] = ["vehicleCount", "quantity", "materialAmountRaw", "materialAmount", "oilFee", "freightFee", "supplyTotal", "payment"],
            ["production-daily"] = ["planOutput", "actualProduction", "productionHours", "ac13Production", "ac16Production", "ac20Production", "sma13Production"],
            ["material-receipt"] = ["quantity", "amountRaw", "amount", "grossWeight", "tareWeight", "netWeight"],
            ["inventory-count"] = ["bookQuantity", "actualQuantity", "variance", "profitLossAmountRaw", "profitLossAmount"],
            ["finished-goods-dispatch"] = ["quantity", "amountRaw", "amount", "weighbridgeWeight"],
            ["sales-collection"] = ["newSales", "collection", "contractAmount", "cumulativeSupply", "receivedPayment", "unpaidPayment"],
            ["equipment-maintenance"] = ["runtimeHours", "maintenanceCost", "partsCost"],
            ["fuel-inventory"] = ["dieselPurchase", "issueQuantity", "amountRaw", "amount"],
            ["quality-inspection"] = [],
            ["transport-settlement"] = ["quantity", "freightRaw", "freightAmount", "tripCount"],
            ["safety-inspection"] = []
        };

    [Fact]
    public async Task Built_in_templates_expose_the_twelve_v1_business_forms_with_resolvable_formula_references()
    {
        var templates = await CreateCatalog().ListAsync(default);

        Assert.Equal(12, templates.Count);
        Assert.Equal(ExpectedTemplateIds.OrderBy(id => id), templates.Select(template => template.Id));
        Assert.All(templates, template =>
        {
            Assert.Equal(1, template.Version);
            Assert.NotEmpty(template.Name);
            Assert.NotEmpty(template.Fields);
            Assert.Equal("A4", template.PrintLayout.Paper);
            Assert.All(template.QueryFields, queryField => Assert.Contains(template.Fields, field => field.Key == queryField));

            var fieldsByKey = template.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
            var missingOrWronglyTypedFields = RequiredFields[template.Id]
                .Where(expected => !fieldsByKey.TryGetValue(expected.Key, out var field) || field.DataType != expected.Value)
                .Select(expected => $"{expected.Key}:{expected.Value}");
            Assert.Empty(missingOrWronglyTypedFields);

            var formulaTargets = template.Formulas.Select(formula => formula.Target).ToHashSet(StringComparer.Ordinal);
            var availableFields = fieldsByKey.Keys
                .Where(key => !formulaTargets.Contains(key))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var formula in template.Formulas)
            {
                Assert.All(formula.Operands, operand => Assert.Contains(operand, availableFields));
                Assert.Contains(formula.Target, fieldsByKey.Keys);
                availableFields.Add(formula.Target);
            }
        });
    }

    [Fact]
    public async Task Specification_fields_allow_the_owner_to_enter_any_custom_specification()
    {
        var specificationFields = (await CreateCatalog().ListAsync(default))
            .SelectMany(template => template.Fields)
            .Where(field => field.Key == "specification")
            .ToArray();

        Assert.NotEmpty(specificationFields);
        Assert.All(specificationFields, field => Assert.Equal(FieldDataType.Text, field.DataType));
    }

    [Fact]
    public async Task Built_in_templates_explicitly_mark_only_meaningful_additive_fields()
    {
        var templates = await CreateCatalog().ListAsync(default);

        foreach (var template in templates)
        {
            var actual = template.Fields.Where(field => field.IsSummable).Select(field => field.Key).OrderBy(key => key);
            Assert.Equal(SummableFields[template.Id].OrderBy(key => key), actual);
        }
    }

    [Fact]
    public async Task Quality_inspection_numeric_measurements_do_not_produce_misleading_totals()
    {
        var template = await CreateCatalog().GetAsync("quality-inspection", 1, default);
        var snapshot = new OutputRecordSnapshot(
            "ZJ-202608-0001", 1, template,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            new Dictionary<string, string> { ["companyName"] = "测试公司" },
            """{"rows":[{"date":"2026-08-01","asphaltContent":4.8,"density":2.41,"measuredValue":98.6}]}""",
            "首次封存", DateTimeOffset.Parse("2026-08-07T08:00:00+08:00"), DateTimeOffset.Parse("2026-08-07T10:30:00+08:00"));

        var layout = new RecordLayoutBuilder().Build(snapshot);

        Assert.Empty(layout.Totals);
    }

    [Fact]
    public async Task Asphalt_inventory_template_calculates_the_daily_closing_balance()
    {
        var template = await CreateCatalog().GetAsync("asphalt-inventory", 1, default);

        var result = new TemplateEngine().Calculate(
            template,
            new Dictionary<string, object?>
            {
                ["opening"] = 10m,
                ["purchase"] = 30m,
                ["consumption"] = 12.5m
            });

        Assert.Equal(27.5m, result["closing"]);
    }

    [Fact]
    public async Task Customer_reconciliation_template_rounds_money_and_calculates_receivable_in_formula_order()
    {
        var template = await CreateCatalog().GetAsync("customer-reconciliation", 1, default);

        var result = new TemplateEngine().Calculate(
            template,
            new Dictionary<string, object?>
            {
                ["quantity"] = 238.14m,
                ["unitPrice"] = 135.30m,
                ["oilFee"] = 0m,
                ["freightFee"] = 0m,
                ["openingReceivable"] = 120000m,
                ["payment"] = 10000m
            });

        Assert.Equal(32220.34m, result["materialAmount"]);
        Assert.Equal(32220.34m, result["supplyTotal"]);
        Assert.Equal(142220.34m, result["receivable"]);
    }

    [Fact]
    public async Task Fuel_inventory_template_uses_one_purchase_issue_and_inventory_flow()
    {
        var template = await CreateCatalog().GetAsync("fuel-inventory", 1, default);

        Assert.DoesNotContain(template.Fields, field => field.Key is "receipt" or "consumption" or "closing" or "equipmentOrVehicle");

        var result = new TemplateEngine().Calculate(
            template,
            new Dictionary<string, object?>
            {
                ["opening"] = 100m,
                ["dieselPurchase"] = 20m,
                ["issueQuantity"] = 5m,
                ["unitPrice"] = 6.5m
            });

        Assert.Equal(130m, result["amount"]);
        Assert.Equal(115m, result["inventory"]);
    }

    private static JsonTemplateCatalog CreateCatalog() => new(FindTemplatesDirectory());

    private static string FindTemplatesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AsphaltPlantManager.sln")))
            {
                return Path.Combine(directory.FullName, "templates");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到仓库根目录中的 templates 目录。");
    }
}
