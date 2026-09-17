namespace AsphaltPlantManager.Core.Receivables;

public interface ICustomerReceivableRepository
{
    Task<IReadOnlyList<CustomerReceivable>> ListAsync(string? customerName, CancellationToken cancellationToken);

    Task UpsertAsync(CustomerReceivable receivable, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
