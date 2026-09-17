namespace AsphaltPlantManager.Core.Output;

public interface IPrintService
{
    Task<PrintPreviewDocument> PreviewAsync(OutputRecordSnapshot snapshot, CancellationToken cancellationToken);

    Task PrintAsync(
        OutputRecordSnapshot snapshot,
        PrinterSettings settings,
        CancellationToken cancellationToken);
}

public sealed record PrinterSettings(string? PrinterName = null, int Copies = 1);

public enum OutputOrientation
{
    Portrait,
    Landscape
}

public sealed record PrintPreviewDocument(
    double PageWidthPoints,
    double PageHeightPoints,
    double MarginPoints,
    OutputOrientation Orientation,
    string Title,
    string CompanyName,
    string Period,
    string Metadata,
    IReadOnlyList<string> ColumnHeaders,
    IReadOnlyList<IReadOnlyList<PrintCellValue>> Rows,
    IReadOnlyList<string> Totals,
    string Remarks,
    IReadOnlyList<string> SignatureLabels,
    string StampLabel,
    string FooterMetadata);

public sealed record PrintCellValue(object? Value, OutputValueKind Kind, string DisplayText);

public enum OutputValueKind
{
    Text,
    Date,
    Number,
    Integer
}
