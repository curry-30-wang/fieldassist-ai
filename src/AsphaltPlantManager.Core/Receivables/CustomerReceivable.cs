namespace AsphaltPlantManager.Core.Receivables;

public enum CustomerReceivableStatus
{
    Unpaid,
    PartiallyPaid,
    Settled
}

public sealed record CustomerReceivable
{
    public CustomerReceivable(
        Guid id,
        string customerName,
        decimal receivableAmount,
        decimal paidAmount,
        DateOnly? dueDate,
        string note,
        DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("欠款记录编号不能为空。", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(customerName);
        if (receivableAmount < 0) throw new ArgumentOutOfRangeException(nameof(receivableAmount));
        if (paidAmount < 0) throw new ArgumentOutOfRangeException(nameof(paidAmount));

        Id = id;
        CustomerName = customerName.Trim();
        ReceivableAmount = decimal.Round(receivableAmount, 2, MidpointRounding.AwayFromZero);
        PaidAmount = decimal.Round(paidAmount, 2, MidpointRounding.AwayFromZero);
        DueDate = dueDate;
        Note = note?.Trim() ?? string.Empty;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public string CustomerName { get; }
    public decimal ReceivableAmount { get; }
    public decimal PaidAmount { get; }
    public DateOnly? DueDate { get; }
    public string Note { get; }
    public DateTimeOffset UpdatedAt { get; }
    public decimal RemainingAmount => Math.Max(0m, ReceivableAmount - PaidAmount);
    public CustomerReceivableStatus Status => RemainingAmount == 0m
        ? CustomerReceivableStatus.Settled
        : PaidAmount == 0m ? CustomerReceivableStatus.Unpaid : CustomerReceivableStatus.PartiallyPaid;
    public string StatusText => Status switch
    {
        CustomerReceivableStatus.Unpaid => "未收",
        CustomerReceivableStatus.PartiallyPaid => "部分收款",
        _ => "已结清"
    };

    public CustomerReceivable Update(
        decimal receivableAmount,
        decimal paidAmount,
        DateOnly? dueDate,
        string note,
        DateTimeOffset updatedAt) => new(Id, CustomerName, receivableAmount, paidAmount, dueDate, note, updatedAt);
}
