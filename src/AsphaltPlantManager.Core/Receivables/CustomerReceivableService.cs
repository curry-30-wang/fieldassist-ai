namespace AsphaltPlantManager.Core.Receivables;

public sealed class CustomerReceivableService
{
    private readonly ICustomerReceivableRepository _repository;

    public CustomerReceivableService(ICustomerReceivableRepository repository) => _repository = repository;

    public Task<IReadOnlyList<CustomerReceivable>> ListAsync(string? customerName, CancellationToken cancellationToken) =>
        _repository.ListAsync(string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim(), cancellationToken);

    public Task SaveAsync(CustomerReceivable receivable, CancellationToken cancellationToken) =>
        _repository.UpsertAsync(receivable, cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        _repository.DeleteAsync(id, cancellationToken);
}
