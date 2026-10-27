using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;

using static SupportToolkit.Tests.CustomerMappingTestSupport;

namespace SupportToolkit.Tests;

public class CustomerMappingCandidateRankerTests
{
    [Fact]
    public async Task UniqueContainment_IsSuggestedWithLowerScoreAndReason()
    {
        var repository = new InMemoryCustomerMappingRepository();

        var result =
            await new CustomerMappingCandidateRanker(repository)
                .RankAsync(
                    [new("t1", "Nordic Group (FGT Tables)")],
                    [new("BC-1", "FGT Tables ApS")]);

        var suggestion = Assert.Single(result.Added);

        Assert.Equal(CustomerMappingState.Suggested, suggestion.State);
        Assert.Equal(CustomerMatchMethod.LooseName, suggestion.MatchMethod);
        Assert.Equal(0.35, suggestion.MatchScore);
        Assert.Contains("Containment", suggestion.Notes);
        Assert.True(
            suggestion.MatchScore
            < CustomerMappingSuggester.ScoreFor(CustomerMatchKey.Name));
        Assert.Empty(result.Exceptions);
        Assert.Single(await repository.GetAllAsync());
    }

    [Fact]
    public async Task NameWithATypo_IsSuggestedAsNearExact()
    {
        var result =
            await new CustomerMappingCandidateRanker(
                new InMemoryCustomerMappingRepository())
                .RankAsync(
                    [new("t1", "Northwind Traders")],
                    [new("BC-1", "Northwnd Traders ApS")]);

        var suggestion = Assert.Single(result.Added);

        Assert.Equal(0.4, suggestion.MatchScore);
        Assert.Contains("NearExact", suggestion.Notes);
    }

    [Fact]
    public async Task CustomersDifferingOnlyByNumber_AreNeverSuggested()
    {
        var result =
            await new CustomerMappingCandidateRanker(
                new InMemoryCustomerMappingRepository())
                .RankAsync(
                    [new("t1", "Hansen Byg 1")],
                    [new("BC-2", "Hansen Byg 2")]);

        Assert.Empty(result.Added);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task UniqueExactAfterNormalization_IsSuggested()
    {
        var result =
            await new CustomerMappingCandidateRanker(
                new InMemoryCustomerMappingRepository())
                .RankAsync(
                    [new("t1", "NORTHWIND-TRADERS")],
                    [new("BC-1", "Northwind Traders")]);

        Assert.Equal(0.45, Assert.Single(result.Added).MatchScore);
    }

    [Fact]
    public async Task OneTenantContainedInSeveralCustomers_IsAnExceptionNotASuggestion()
    {
        var result =
            await new CustomerMappingCandidateRanker(
                new InMemoryCustomerMappingRepository())
                .RankAsync(
                    [new("t1", "Fabrikam")],
                    [
                        new("BC-1", "Fabrikam Logistics"),
                        new("BC-2", "Fabrikam Retail")
                    ]);

        Assert.Empty(result.Added);
        Assert.NotEmpty(result.Exceptions);
        Assert.All(
            result.Exceptions,
            exception => Assert.Equal(
                CustomerMatchExceptionKind.OneToMany,
                exception.Kind));
    }

    [Fact]
    public async Task TwoTenantsForOneCustomer_AreAConflict()
    {
        var result =
            await new CustomerMappingCandidateRanker(
                new InMemoryCustomerMappingRepository())
                .RankAsync(
                    [
                        new("t1", "Northwind Traders"),
                        new("t2", "Northwind Traders Nord")
                    ],
                    [new("BC-1", "Northwind Traders")]);

        // t1 is exact, t2 only contained: BC-1 has one best tenant, but
        // t2 also competes for it, so nothing is suggested for t2 and
        // the exact pair is still suggested.
        var added = Assert.Single(result.Added);
        Assert.Equal("t1", added.AcronisTenantId);
    }

    [Fact]
    public async Task TwoTenantsEquallyGoodForOneCustomer_AreNotSuggested()
    {
        var result =
            await new CustomerMappingCandidateRanker(
                new InMemoryCustomerMappingRepository())
                .RankAsync(
                    [
                        new("t1", "Northwind Traders Nord"),
                        new("t2", "Northwind Traders Syd")
                    ],
                    [new("BC-1", "Northwind Traders")]);

        Assert.Empty(result.Added);
        Assert.Contains(
            result.Exceptions,
            exception => exception.Kind
                == CustomerMatchExceptionKind.ManyToOne);
    }

    [Fact]
    public async Task Overlap_IsReportedButNeverSuggested()
    {
        var result =
            await new CustomerMappingCandidateRanker(
                new InMemoryCustomerMappingRepository())
                .RankAsync(
                    [new("t1", "Alpine Consulting Services Nord")],
                    [new("BC-1", "Alpine Consulting Services Syd")]);

        Assert.Empty(result.Added);

        var exception = Assert.Single(result.Exceptions);

        Assert.Equal(
            CustomerMatchExceptionKind.WeakNameCandidate,
            exception.Kind);
    }

    [Theory]
    [InlineData(CustomerMappingState.Suggested)]
    [InlineData(CustomerMappingState.Confirmed)]
    public async Task PartiesInExistingMappings_AreNotOverridden(
        CustomerMappingState state)
    {
        var repository = new InMemoryCustomerMappingRepository();

        await repository.AddAsync(
            Mapping("t1", "BC-OTHER", state, CustomerMatchMethod.Cvr));

        var result =
            await new CustomerMappingCandidateRanker(repository)
                .RankAsync(
                    [new("t1", "Northwind Traders")],
                    [new("BC-1", "Northwind Traders")]);

        Assert.Empty(result.Added);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task RejectedPair_IsNotSuggestedAgain()
    {
        var repository = new InMemoryCustomerMappingRepository();

        await repository.AddAsync(
            Mapping(
                "t1",
                "BC-1",
                CustomerMappingState.Rejected,
                CustomerMatchMethod.LooseName));

        var result =
            await new CustomerMappingCandidateRanker(repository)
                .RankAsync(
                    [new("t1", "Northwind Traders")],
                    [new("BC-1", "Northwind Traders")]);

        Assert.Empty(result.Added);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task GenericOnlyNames_NeverProduceCandidates()
    {
        var result =
            await new CustomerMappingCandidateRanker(
                new InMemoryCustomerMappingRepository())
                .RankAsync(
                    [new("t1", "Holding Group ApS")],
                    [new("BC-1", "Holding Group A/S")]);

        Assert.Empty(result.Candidates);
        Assert.Empty(result.Added);
    }
}
