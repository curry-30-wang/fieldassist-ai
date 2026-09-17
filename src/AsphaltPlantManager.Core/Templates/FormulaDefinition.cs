namespace AsphaltPlantManager.Core.Templates;

public sealed record FormulaDefinition(
    string Target,
    FormulaOperator Operator,
    string[] Operands,
    int DecimalPlaces = 2);

public enum FormulaOperator
{
    Add,
    Subtract,
    Multiply,
    Sum,
    Round
}
