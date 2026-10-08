using LoanApp.Application.Applications;
using LoanApp.Domain;
using LoanApp.Domain.Customers;
using LoanApp.Domain.Decisions;

namespace LoanApp.UnitTests;

internal static class TestData
{
    public static Applicant Applicant(string ssn = "222-33-4444", string state = "TX", string firstName = "Ann") =>
        new(Ssn.Parse(ssn), firstName, "Lee", new Address("1 Main St", "Austin", state, "78701"), "Acme");

    public static LoanRequest Request(string ssn = "222-33-4444", string state = "TX", decimal amount = 5_000) =>
        new(Applicant(ssn, state), amount);

    public static SubmitApplicationCommand Command(string ssn = "222-33-4444", string state = "TX", decimal amount = 5_000, string firstName = "Ann") =>
        new(firstName, "Lee", "1 Main St", "Austin", state, "78701", "Acme", amount, ssn);
}
