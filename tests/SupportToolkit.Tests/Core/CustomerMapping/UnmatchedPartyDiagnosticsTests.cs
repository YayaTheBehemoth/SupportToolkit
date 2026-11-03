using SupportToolkit.Cli.Commands;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Tests;

public class UnmatchedPartyDiagnosticsTests
{
    private static readonly IReadOnlySet<string> None =
        new HashSet<string>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoItems =
        new Dictionary<string, IReadOnlyList<string>>();

    [Fact]
    public void NothingSimilar_IsReportedAsNoSimilarCustomer()
    {
        var diagnosis =
            Single(
                new CustomerParty("t1", "Northwind Traders"),
                [new("BC-1", "Contoso Supplies")]);

        Assert.Equal(UnmatchedReason.NoSimilarCustomer, diagnosis.Reason);
        Assert.Empty(diagnosis.Candidates);
    }

    [Fact]
    public void SimilarCustomerOutsideTheMatchedSet_MeansItHoldsNoServiceItem()
    {
        var diagnosis =
            Single(
                new CustomerParty("t1", "Northwind Traders"),
                [new("BC-1", "Northwind Traders ApS")],
                matched: None,
                items: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["BC-1"] = ["OTHER-ITEM"]
                });

        Assert.Equal(
            UnmatchedReason.SimilarCustomerNotInScope,
            diagnosis.Reason);

        var candidate = Assert.Single(diagnosis.Candidates);

        Assert.False(candidate.InMatchedSet);
        Assert.Equal(["OTHER-ITEM"], candidate.Items);
        Assert.Equal(LooseNameTier.Exact, candidate.Tier);
    }

    [Fact]
    public void CandidateAlreadyLinkedElsewhere_IsReportedAsTaken()
    {
        var diagnosis =
            Single(
                new CustomerParty("t2", "Northwind Traders"),
                [new("BC-1", "Northwind Traders ApS")],
                matched: new HashSet<string> { "BC-1" },
                linked: new HashSet<string> { "BC-1" });

        Assert.Equal(
            UnmatchedReason.CandidateTakenByAnotherParty,
            diagnosis.Reason);
    }

    [Fact]
    public void OnlyWordOverlap_IsReportedAsWeak()
    {
        var diagnosis =
            Single(
                new CustomerParty("t1", "Alpine Consulting Services Nord"),
                [new("BC-1", "Alpine Consulting Services Syd")],
                matched: new HashSet<string> { "BC-1" });

        Assert.Equal(
            UnmatchedReason.OnlyWeakNameOverlap,
            diagnosis.Reason);
    }

    [Fact]
    public void ResolverFlaggedParty_IsReportedAsAmbiguous()
    {
        var diagnosis =
            Single(
                new CustomerParty("t1", "Adventure Works"),
                [
                    new("BC-1", "Adventure Works ApS"),
                    new("BC-2", "Adventure Works I/S")
                ],
                matched: new HashSet<string> { "BC-1", "BC-2" },
                flagged: new HashSet<string> { "t1" });

        Assert.Equal(
            UnmatchedReason.AmbiguousOrConflicting,
            diagnosis.Reason);

        Assert.Equal(2, diagnosis.Candidates.Count);
    }

    [Fact]
    public void WeakNameCandidateException_IsNotReportedAsAmbiguity()
    {
        var acronis =
            new AcronisPartyBuild(
                [new("t1", "Alpine Consulting Services Nord")],
                false,
                null);

        var customers =
            new ScopedCustomerParties(
                [new("BC-1", "Alpine Consulting Services Syd")],
                0,
                0);

        var result =
            new AutoMappingResult(
                [],
                [],
                [
                    new CustomerMatchException(
                        CustomerMatchExceptionKind.WeakNameCandidate,
                        CustomerMatchKey.Name,
                        ["t1"],
                        ["BC-1"])
                ],
                ["t1"],
                ["BC-1"]);

        var diagnosis =
            Assert.Single(
                CustomerMatchingDevCommand.Explain(
                    acronis,
                    customers,
                    customers,
                    new SmartSubscriptionEvidence([], []),
                    result));

        Assert.Equal(UnmatchedReason.OnlyWeakNameOverlap, diagnosis.Reason);
    }

    [Fact]
    public void Report_ShowsWhatTheMatcherSees_AndOddCharacters()
    {
        var diagnoses =
            UnmatchedPartyDiagnostics.Diagnose(
                [new("t1", "Nordic Foods")],
                [new("BC-1", "Nordic Foods ApS")],
                None,
                None,
                None,
                NoItems);

        var writer = new StringWriter();

        CustomerMatchingDevCommand.WriteDiagnoses(writer, diagnoses, true);

        var text = writer.ToString();

        Assert.Contains("sees: \"nordicfoods\"; odd: U+00A0", text);
        Assert.Contains("sees: \"nordicfoods\"", text);
    }

    [Fact]
    public void BestCandidatesAreRankedByTier_AndLimitedToThree()
    {
        var diagnosis =
            Single(
                new CustomerParty("t1", "Fabrikam"),
                [
                    new("BC-1", "Fabrikam Logistics"),
                    new("BC-2", "Fabrikam"),
                    new("BC-3", "Fabrikam Retail"),
                    new("BC-4", "Fabrikam Nordic")
                ],
                matched: new HashSet<string> { "BC-1", "BC-2", "BC-3", "BC-4" });

        Assert.Equal(3, diagnosis.Candidates.Count);
        Assert.Equal("BC-2", diagnosis.Candidates[0].CustomerId);
        Assert.Equal(LooseNameTier.Exact, diagnosis.Candidates[0].Tier);
    }

    [Fact]
    public void Report_CountsByReason_AndHidesNamesUnlessAsked()
    {
        var diagnoses =
            UnmatchedPartyDiagnostics.Diagnose(
                [
                    new("t1", "Secret Tenant One"),
                    new("t2", "Zzz Unknown")
                ],
                [new("BC-1", "Secret Tenant One ApS")],
                None,
                None,
                None,
                NoItems);

        var hidden = new StringWriter();

        CustomerMatchingDevCommand.WriteDiagnoses(hidden, diagnoses, false);

        Assert.Contains("Tenants without an accepted customer: 2", hidden.ToString());
        Assert.Contains("similar customer holds none of the service items: 1", hidden.ToString());
        Assert.Contains("no similar customer in Business Central: 1", hidden.ToString());
        Assert.DoesNotContain("Secret Tenant One", hidden.ToString());
        Assert.DoesNotContain("BC-1", hidden.ToString());

        var shown = new StringWriter();

        CustomerMatchingDevCommand.WriteDiagnoses(shown, diagnoses, true);

        Assert.Contains("\"Secret Tenant One\"", shown.ToString());
        Assert.Contains("BC BC-1 \"Secret Tenant One ApS\" [Exact] NOT in matched set", shown.ToString());
    }

    private static UnmatchedPartyDiagnosis Single(
        CustomerParty tenant,
        IReadOnlyList<CustomerParty> pool,
        IReadOnlySet<string>? matched = null,
        IReadOnlySet<string>? linked = null,
        IReadOnlySet<string>? flagged = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? items = null)
    {
        return Assert.Single(
            UnmatchedPartyDiagnostics.Diagnose(
                [tenant],
                pool,
                matched ?? None,
                linked ?? None,
                flagged ?? None,
                items ?? NoItems));
    }
}
