namespace LoanApp.Domain.Decisions;

public interface ISsnBlacklist
{
    bool Contains(Ssn ssn);
}
