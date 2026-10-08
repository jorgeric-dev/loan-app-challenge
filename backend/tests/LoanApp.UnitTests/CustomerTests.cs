using LoanApp.Domain;
using LoanApp.Domain.Customers;

namespace LoanApp.UnitTests;

public class CustomerTests
{
    [Fact]
    public void Create_opens_one_application_for_the_customer()
    {
        var customer = Customer.Create(TestData.Applicant(), 5_000);

        Assert.Equal(customer.Id, customer.Application.CustomerId);
        Assert.Equal(5_000, customer.Application.RequestedAmount);
    }

    [Fact]
    public void Resubmit_updates_the_customer_and_its_existing_application()
    {
        var customer = Customer.Create(TestData.Applicant(firstName: "Ann"), 5_000);
        var applicationId = customer.Application.Id;

        customer.Resubmit(TestData.Applicant(firstName: "Annie", state: "CA"), 9_000);

        Assert.Equal("Annie", customer.FirstName);
        Assert.Equal("CA", customer.Address.State);
        Assert.Equal(applicationId, customer.Application.Id);
        Assert.Equal(9_000, customer.Application.RequestedAmount);
    }

    [Fact]
    public void Resubmit_rejects_a_different_ssn()
    {
        var customer = Customer.Create(TestData.Applicant(ssn: "222-33-4444"), 5_000);

        Assert.Throws<DomainException>(() => customer.Resubmit(TestData.Applicant(ssn: "999-88-7777"), 9_000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Requested_amount_must_be_positive(decimal amount) =>
        Assert.Throws<DomainException>(() => Customer.Create(TestData.Applicant(), amount));

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("12a-45-6789")]
    [InlineData("")]
    public void Ssn_requires_nine_digits(string input) =>
        Assert.Throws<DomainException>(() => Ssn.Parse(input));

    [Fact]
    public void Ssn_ignores_formatting_and_masks_itself_when_printed()
    {
        var ssn = Ssn.Parse("123-45 6789");

        Assert.Equal("123456789", ssn.Value);
        Assert.Equal("***-**-6789", ssn.ToString());
    }
}
