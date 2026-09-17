namespace AsphaltPlantManager.Core.Receivables;

public interface ICustomerReceivableExporter
{
    Task<string> ExportAsync(CustomerReceivable receivable, string destinationDirectory, CancellationToken cancellationToken);
}
