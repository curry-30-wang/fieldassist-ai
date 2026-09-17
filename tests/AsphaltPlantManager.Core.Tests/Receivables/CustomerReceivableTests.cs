using AsphaltPlantManager.Core.Receivables;
using FluentAssertions;
using Xunit;

namespace AsphaltPlantManager.Core.Tests.Receivables;

public sealed class CustomerReceivableTests
{
    [Fact]
    public void Calculates_remaining_amount_and_status_for_unpaid_partially_paid_and_settled()
    {
        var unpaid = Create(100m, 0m);
        var partial = Create(100m, 25m);
        var settled = Create(100m, 120m);

        unpaid.RemainingAmount.Should().Be(100m);
        unpaid.Status.Should().Be(CustomerReceivableStatus.Unpaid);
        partial.RemainingAmount.Should().Be(75m);
        partial.Status.Should().Be(CustomerReceivableStatus.PartiallyPaid);
        settled.RemainingAmount.Should().Be(0m);
        settled.Status.Should().Be(CustomerReceivableStatus.Settled);
    }

    [Fact]
    public void Rejects_empty_customer_and_negative_amounts()
    {
        var emptyCustomer = () => new CustomerReceivable(Guid.NewGuid(), " ", 1m, 0m, null, "", DateTimeOffset.UtcNow);
        var negativeReceivable = () => new CustomerReceivable(Guid.NewGuid(), "客户", -1m, 0m, null, "", DateTimeOffset.UtcNow);
        var negativePaid = () => new CustomerReceivable(Guid.NewGuid(), "客户", 1m, -1m, null, "", DateTimeOffset.UtcNow);

        emptyCustomer.Should().Throw<ArgumentException>();
        negativeReceivable.Should().Throw<ArgumentOutOfRangeException>();
        negativePaid.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static CustomerReceivable Create(decimal receivable, decimal paid) => new(Guid.NewGuid(), "客户", receivable, paid, new DateOnly(2026, 8, 31), "备注", DateTimeOffset.UtcNow);
}
