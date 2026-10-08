namespace LoanApp.Domain.Customers;

public sealed class LoanApplication
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public decimal RequestedAmount { get; private set; }

    private LoanApplication() { } // EF Core

    internal LoanApplication(Guid customerId, decimal requestedAmount)
    {
        Id = Guid.NewGuid();
        CustomerId = customerId;
        ChangeRequestedAmount(requestedAmount);
    }

    internal void ChangeRequestedAmount(decimal requestedAmount)
    {
        if (requestedAmount <= 0)
            throw new DomainException("Requested amount must be greater than zero.");
        RequestedAmount = requestedAmount;
    }
}
