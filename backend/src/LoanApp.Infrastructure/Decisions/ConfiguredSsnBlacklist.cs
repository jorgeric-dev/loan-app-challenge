using LoanApp.Domain;
using LoanApp.Domain.Decisions;
using Microsoft.Extensions.Options;

namespace LoanApp.Infrastructure.Decisions;

public sealed class SsnBlacklistOptions
{
    public const string Section = "SsnBlacklist";
    public string[] Ssns { get; set; } = [];
}

/// <summary>Blacklist read from configuration. Swap for a database/provider-backed one without touching the rule.</summary>
public sealed class ConfiguredSsnBlacklist(IOptions<SsnBlacklistOptions> options) : ISsnBlacklist
{
    private readonly HashSet<Ssn> _ssns = options.Value.Ssns.Select(Ssn.Parse).ToHashSet();

    public bool Contains(Ssn ssn) => _ssns.Contains(ssn);
}
