using LoanApp.Domain.Customers;

namespace LoanApp.Domain.Decisions;

/// <summary>What the rule engine decides on: who is asking and how much.</summary>
public sealed record LoanRequest(Applicant Applicant, decimal RequestedAmount);
