namespace LoanApp.Application.Applications;

public enum ApplicationStatus
{
    Approved,
    Denied,
}

public sealed record SubmitApplicationResult(
    ApplicationStatus Status,
    Guid? CustomerId,
    Guid? ApplicationId,
    bool IsReturningCustomer,
    IReadOnlyList<string> DenialReasons)
{
    public static SubmitApplicationResult Approved(Guid customerId, Guid applicationId, bool isReturningCustomer) =>
        new(ApplicationStatus.Approved, customerId, applicationId, isReturningCustomer, []);

    public static SubmitApplicationResult Denied(IReadOnlyList<string> reasonCodes) =>
        new(ApplicationStatus.Denied, null, null, false, reasonCodes);
}
