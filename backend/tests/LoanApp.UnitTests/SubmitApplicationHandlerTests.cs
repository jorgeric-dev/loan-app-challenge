using LoanApp.Application.Abstractions;
using LoanApp.Application.Applications;
using LoanApp.Application.Diagnostics;
using LoanApp.Application.Events;
using LoanApp.Domain;
using LoanApp.Domain.Customers;
using LoanApp.Domain.Decisions;
using LoanApp.Domain.Decisions.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoanApp.UnitTests;

public class SubmitApplicationHandlerTests
{
    private readonly InMemoryCustomers _customers = new();
    private readonly RecordingPublisher _events = new();
    private readonly RecordingUnitOfWork _unitOfWork = new();
    private readonly SubmitApplicationHandler _handler;

    public SubmitApplicationHandlerTests() =>
        _handler = new SubmitApplicationHandler(new DecisionEngine([new RestrictedStateRule()]), _customers, _events, _unitOfWork,
            new ApplicationMetrics(new TestMeterFactory()), NullLogger<SubmitApplicationHandler>.Instance);

    [Fact]
    public async Task New_customer_is_created_and_a_create_event_is_published()
    {
        var result = await _handler.HandleAsync(TestData.Command(), default);

        Assert.Equal(ApplicationStatus.Approved, result.Status);
        Assert.False(result.IsReturningCustomer);
        var customer = Assert.Single(_customers.All);
        var published = Assert.IsType<LoanApplicationApproved>(Assert.Single(_events.Published));
        Assert.False(published.IsReturningCustomer);
        Assert.Equal(customer.Id, published.Customer.Id);
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task Returning_customer_updates_the_same_records_and_publishes_an_update_event()
    {
        var first = await _handler.HandleAsync(TestData.Command(firstName: "Ann", amount: 5_000), default);

        var second = await _handler.HandleAsync(TestData.Command(firstName: "Annie", amount: 9_000), default);

        Assert.True(second.IsReturningCustomer);
        Assert.Equal(first.CustomerId, second.CustomerId);
        Assert.Equal(first.ApplicationId, second.ApplicationId);
        var customer = Assert.Single(_customers.All);
        Assert.Equal("Annie", customer.FirstName);
        Assert.Equal(9_000, customer.Application.RequestedAmount);
        var update = Assert.IsType<LoanApplicationApproved>(_events.Published[^1]);
        Assert.True(update.IsReturningCustomer);
        Assert.Equal(9_000, update.Application.RequestedAmount);
    }

    [Fact]
    public async Task Denied_application_saves_nothing_and_publishes_nothing()
    {
        var result = await _handler.HandleAsync(TestData.Command(state: "NY"), default);

        Assert.Equal(ApplicationStatus.Denied, result.Status);
        Assert.Equal([RestrictedStateRule.Code], result.DenialReasons);
        Assert.Empty(_customers.All);
        Assert.Empty(_events.Published);
        Assert.Equal(0, _unitOfWork.Commits);
    }

    private sealed class InMemoryCustomers : ICustomerRepository
    {
        public List<Customer> All { get; } = [];

        public Task<Customer?> FindBySsnAsync(Ssn ssn, CancellationToken cancellationToken) =>
            Task.FromResult(All.SingleOrDefault(c => c.Ssn == ssn));

        public void Add(Customer customer) => All.Add(customer);
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<object> Published { get; } = [];

        public void Publish<TEvent>(TEvent integrationEvent) where TEvent : IIntegrationEvent => Published.Add(integrationEvent);
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int Commits { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            Commits++;
            return Task.CompletedTask;
        }
    }
}
