using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;

using static SupportToolkit.Tests.CustomerMappingTestSupport;

namespace SupportToolkit.Tests;

public class AutoCustomerMappingResolverTests
{
    [Fact]
    public async Task SharedCvr_IsAcceptedWhateverTheNames()
    {
        var result =
            await Resolve(
                [new("t1", "Completely Different", Cvr: "10000001")],
                [new("BC-1", "Northwind Traders", Cvr: "10000001")]);

        var accepted = Assert.Single(result.Accepted);

        Assert.Equal(CustomerMatchMethod.Cvr, accepted.MatchMethod);
        Assert.Equal(AutoCustomerMappingResolver.AutomaticActor, accepted.ConfirmedBy);
        Assert.Equal(CustomerMappingState.Confirmed, accepted.State);
        Assert.Equal("BC-1", Assert.Single(result.Pairs).BcCustomerNumber);
        Assert.Empty(result.PendingReview);
        Assert.Empty(result.UnmatchedAcronisIds);
        Assert.Empty(result.UnmatchedBcCustomerNumbers);
    }

    [Fact]
    public async Task ExactNormalizedName_IsAccepted()
    {
        var result =
            await Resolve(
                [new("t1", "NORTHWIND TRADERS")],
                [new("BC-1", "Northwind Traders ApS")]);

        Assert.Equal(
            CustomerMatchMethod.Name,
            Assert.Single(result.Accepted).MatchMethod);
    }

    [Fact]
    public async Task LooseExactAndContainmentNames_AreBothAccepted()
    {
        var result =
            await Resolve(
                [
                    new("t1", "Holding company (FGT Tables)"),
                    new("t2", "Fabrikam")
                ],
                [
                    new("BC-1", "FGT Tables"),
                    new("BC-2", "Fabrikam Logistics")
                ]);

        Assert.Equal(2, result.Accepted.Count);
        Assert.All(
            result.Accepted,
            mapping => Assert.Equal(
                CustomerMatchMethod.LooseName,
                mapping.MatchMethod));
        Assert.Empty(result.PendingReview);
    }

    [Fact]
    public async Task WeakOverlap_IsNeverAccepted()
    {
        var result =
            await Resolve(
                [new("t1", "Alpine Consulting Services Nord")],
                [new("BC-1", "Alpine Consulting Services Syd")]);

        Assert.Empty(result.Accepted);
        Assert.Empty(result.PendingReview);
        Assert.Single(result.Exceptions);
    }

    [Fact]
    public async Task PendingSuggestions_AreNotCountedAsUnmatched()
    {
        var result =
            await new AutoCustomerMappingResolver(
                new AutoMappingPolicy(0.45))
                .ResolveAsync(
                    [
                        new("t1", "Fabrikam"),
                        new("t2", "Unrelated Tenant")
                    ],
                    [
                        new("BC-1", "Fabrikam Logistics"),
                        new("BC-2", "Unrelated Customer")
                    ]);

        Assert.Single(result.PendingReview);
        Assert.Equal(["t2"], result.UnmatchedAcronisIds);
        Assert.Equal(["BC-2"], result.UnmatchedBcCustomerNumbers);
    }

    [Fact]
    public async Task CvrAndNameDisagreeing_AcceptsNothing()
    {
        var result =
            await Resolve(
                [new("t1", "Northwind Traders", Cvr: "10000002")],
                [
                    new("BC-1", "Northwind Traders", Cvr: "10000001"),
                    new("BC-2", "Contoso Supplies", Cvr: "10000002")
                ]);

        Assert.Empty(result.Accepted);
        Assert.Contains(
            result.Exceptions,
            exception => exception.Kind
                == CustomerMatchExceptionKind.KeyConflict);
    }

    [Fact]
    public async Task SameNameForTwoCustomers_IsNeverGuessed()
    {
        var result =
            await Resolve(
                [new("t1", "Adventure Works")],
                [
                    new("BC-1", "Adventure Works ApS"),
                    new("BC-2", "Adventure Works I/S")
                ]);

        Assert.Empty(result.Accepted);
        Assert.NotEmpty(result.Exceptions);
        Assert.Single(result.UnmatchedAcronisIds);
        Assert.Equal(2, result.UnmatchedBcCustomerNumbers.Count);
    }

    [Fact]
    public async Task PolicyNone_LeavesEverythingForReview()
    {
        var result =
            await new AutoCustomerMappingResolver(AutoMappingPolicy.None)
                .ResolveAsync(
                    [new("t1", "Northwind Traders")],
                    [new("BC-1", "Northwind Traders")]);

        Assert.Empty(result.Accepted);
        Assert.Single(result.PendingReview);
    }

    [Fact]
    public async Task StoredDecisions_TakePrecedenceOverAutomaticOnes()
    {
        var repository =
            await RepositoryWith(
                Mapping(
                    "t1",
                    "BC-9",
                    CustomerMappingState.Confirmed,
                    CustomerMatchMethod.Manual));

        var result =
            await new AutoCustomerMappingResolver()
                .ResolveAsync(
                    [new("t1", "Northwind Traders")],
                    [new("BC-1", "Northwind Traders")],
                    repository);

        Assert.Empty(result.Accepted);
        Assert.Empty(result.PendingReview);
    }

    [Fact]
    public async Task RejectedPair_IsNotAutoAccepted()
    {
        var repository =
            await RepositoryWith(
                Mapping(
                    "t1",
                    "BC-1",
                    CustomerMappingState.Rejected,
                    CustomerMatchMethod.Name));

        var result =
            await new AutoCustomerMappingResolver()
                .ResolveAsync(
                    [new("t1", "Northwind Traders")],
                    [new("BC-1", "Northwind Traders")],
                    repository);

        Assert.Empty(result.Accepted);
    }

    [Fact]
    public async Task EmptyInputs_ResolveToNothing()
    {
        var result = await Resolve([], []);

        Assert.Empty(result.Accepted);
        Assert.Empty(result.UnmatchedAcronisIds);
        Assert.Empty(result.UnmatchedBcCustomerNumbers);
    }

    private static Task<AutoMappingResult> Resolve(
        IReadOnlyList<CustomerParty> acronis,
        IReadOnlyList<CustomerParty> businessCentral)
    {
        return new AutoCustomerMappingResolver()
            .ResolveAsync(acronis, businessCentral);
    }
}
