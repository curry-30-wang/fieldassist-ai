using AsphaltPlantManager.Core.Templates;

namespace AsphaltPlantManager.Core.Output;

public interface IExcelFormWorkflow
{
    Task<ExcelFormDraft> CreateDraftAsync(
        TemplateDefinition template,
        string companyName,
        DateOnly periodStart,
        DateOnly periodEnd,
        string destinationDirectory,
        CancellationToken cancellationToken);

    Task<ExcelFormDraft> CreateEditableDraftAsync(
        OutputRecordSnapshot snapshot,
        string destinationDirectory,
        CancellationToken cancellationToken);

    Task<ImportedExcelForm> ImportAsync(string workbookPath, CancellationToken cancellationToken);
}
