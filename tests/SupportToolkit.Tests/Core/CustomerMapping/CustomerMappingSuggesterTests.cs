using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;

using static SupportToolkit.Tests.CustomerMappingTestSupport;

namespace SupportToolkit.Tests;

public class CustomerMappingSuggesterTests
{
    [Fact]
    public async Task OneToOneMatches_AreSuggestedWithStrongestMethod()
    {
        var repository =
            await RepositoryWith();

        var result =
            await Suggester(repository).SuggestAsync(
                [
                    new("t1", "Alpha ApS", "10000001", "it@alpha.test"),
                    new("t2", "Beta", null, "it@beta.test"),
                    new("t3", "Gamma")
                ],
                [
                    new("BC-1", "Alpha A/S", "DK10000001", "x@alpha.test"),
                    new("BC-2", "Beta", null, "y@beta.test"),
                    new("BC-3", "Gamma ApS")
                ]
            );

        Assert.Empty(result.Exceptions);

        Assert.Collection(
            result.Added.OrderBy(mapping => mapping.AcronisTenantId),
            mapping => AssertSuggested(mapping, "t1", "BC-1", CustomerMatchMethod.Cvr, 1.0),
            mapping => AssertSuggested(mapping, "t2", "BC-2", CustomerMatchMethod.EmailDomain, 0.8),
            mapping => AssertSuggested(mapping, "t3", "BC-3", CustomerMatchMethod.Name, 0.5)
        );

        Assert.Equal(
            3,
            (await repository.GetAllAsync()).Count
        );
    }

    [Fact]
    public async Task OneToManyAndManyToOne_GoToExceptions()
    {
        var result =
            await Suggester(await RepositoryWith()).SuggestAsync(
                [
                    new("t1", Cvr: "10000001"),
                    new("t2", Cvr: "10000002"),
                    new("t3", Cvr: "10000002")
                ],
                [
                    new("BC-1", Cvr: "10000001"),
                    new("BC-1B", Cvr: "10000001"),
                    new("BC-2", Cvr: "10000002")
                ]
            );

        Assert.Empty(result.Added);

        Assert.Contains(
            result.Exceptions,
            exception =>
                exception.Kind == CustomerMatchExceptionKind.OneToMany
                && exception.Key == CustomerMatchKey.Cvr
                && exception.AcronisTenantIds.SequenceEqual(["t1"])
        );

        Assert.Contains(
            result.Exceptions,
            exception =>
                exception.Kind == CustomerMatchExceptionKind.ManyToOne
                && exception.BcCustomerNumbers.SequenceEqual(["BC-2"])
        );
    }

    [Fact]
    public async Task KeysPointingToDifferentPartners_AreKeyConflicts()
    {
        var result =
            await Suggester(await RepositoryWith()).SuggestAsync(
                [new("t1", Cvr: "10000001", Email: "it@alpha.test")],
                [
                    new("BC-1", Cvr: "10000001"),
                    new("BC-2", Email: "x@alpha.test")
                ]
            );

        Assert.Empty(result.Added);

        var conflict =
            Assert.Single(result.Exceptions);

        Assert.Equal(CustomerMatchExceptionKind.KeyConflict, conflict.Kind);
        Assert.Equal(new[] { "BC-1", "BC-2" }, conflict.BcCustomerNumbers.Order());
    }

    [Fact]
    public async Task WeakerKey_IsNotUsedWhenStrongerKeyIsAmbiguous()
    {
        var result =
            await Suggester(await RepositoryWith()).SuggestAsync(
                [new("t1", "Alpha", "10000001")],
                [
                    new("BC-1", "Alpha", "10000001"),
                    new("BC-1B", "Alpha Holding", "10000001")
                ]
            );

        Assert.Empty(result.Added);

        Assert.Equal(
            CustomerMatchExceptionKind.OneToMany,
            Assert.Single(result.Exceptions).Kind
        );
    }

    [Fact]
    public async Task RejectedPairs_AreNeverSuggestedAgain()
    {
        var repository =
            await RepositoryWith(
                Mapping("t1", "BC-1", CustomerMappingState.Rejected, CustomerMatchMethod.Cvr)
            );

        var result =
            await Suggester(repository).SuggestAsync(
                [new("t1", Cvr: "10000001")],
                [new("BC-1", Cvr: "10000001")]
            );

        Assert.Empty(result.Added);
        Assert.Single(await repository.GetAllAsync());
    }

    [Fact]
    public async Task PartiesInSuggestedOrConfirmedMappings_AreSkipped()
    {
        var repository =
            await RepositoryWith(
                Mapping("t1", "BC-OTHER", CustomerMappingState.Confirmed),
                Mapping("t-other", "BC-2", CustomerMappingState.Suggested),
                Mapping("t3", "BC-OLD", CustomerMappingState.Retired)
            );

        var result =
            await Suggester(repository).SuggestAsync(
                [
                    new("t1", Cvr: "10000001"),
                    new("t2", Cvr: "10000002"),
                    new("t3", Cvr: "10000003")
                ],
                [
                    new("BC-1", Cvr: "10000001"),
                    new("BC-2", Cvr: "10000002"),
                    new("BC-3", Cvr: "10000003")
                ]
            );

        var added =
            Assert.Single(result.Added);

        Assert.Equal("t3", added.AcronisTenantId);
        Assert.Equal("BC-3", added.BcCustomerNo);
    }

    [Fact]
    public async Task Rerun_DoesNotDuplicateSuggestions()
    {
        var repository =
            await RepositoryWith();

        IReadOnlyList<CustomerParty> acronis =
            [new("t1", Cvr: "10000001")];

        IReadOnlyList<CustomerParty> businessCentral =
            [new("BC-1", Cvr: "10000001")];

        await Suggester(repository).SuggestAsync(acronis, businessCentral);

        var rerun =
            await Suggester(repository).SuggestAsync(acronis, businessCentral);

        Assert.Empty(rerun.Added);
        Assert.Single(await repository.GetAllAsync());
    }

    [Fact]
    public async Task DuplicatePartyIds_AreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Suggester(new InMemoryCustomerMappingRepository())
                .SuggestAsync(
                    [new("t1"), new("T1")],
                    []
                )
        );
    }

    [Fact]
    public void NameScore_IsBelowStrongerKeys()
    {
        Assert.True(
            CustomerMappingSuggester.ScoreFor(CustomerMatchKey.Name)
            < CustomerMappingSuggester.ScoreFor(CustomerMatchKey.EmailDomain)
        );

        Assert.True(
            CustomerMappingSuggester.ScoreFor(CustomerMatchKey.EmailDomain)
            < CustomerMappingSuggester.ScoreFor(CustomerMatchKey.Cvr)
        );
    }

    private static CustomerMappingSuggester Suggester(
        ICustomerMappingRepository repository)
    {
        return new CustomerMappingSuggester(
            repository,
            new FixedTimeProvider()
        );
    }

    private static void AssertSuggested(
        CustomerMappingRecord mapping,
        string tenant,
        string customer,
        CustomerMatchMethod method,
        double score)
    {
        Assert.Equal(tenant, mapping.AcronisTenantId);
        Assert.Equal(customer, mapping.BcCustomerNo);
        Assert.Equal(CustomerMappingState.Suggested, mapping.State);
        Assert.Equal(method, mapping.MatchMethod);
        Assert.Equal(score, mapping.MatchScore);
        Assert.Equal(Now, mapping.SuggestedAt);
        Assert.Null(mapping.ConfirmedAt);
    }
}
