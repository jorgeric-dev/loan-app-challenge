namespace LoanApp.Domain.Decisions;

/// <param name="Code">Stable machine-readable code (used by the UI to pick the message shown to the applicant).</param>
/// <param name="Description">Internal explanation, for logs.</param>
public sealed record DenialReason(string Code, string Description);
