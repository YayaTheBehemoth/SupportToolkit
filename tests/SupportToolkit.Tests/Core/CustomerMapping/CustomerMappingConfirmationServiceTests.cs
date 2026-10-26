using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

using static SupportToolkit.Tests.CustomerMappingTestSupport;

namespace SupportToolkit.Tests;

public class CustomerMappingConfirmationServiceTests
{
    [Fact]
    public async Task Confirm_RecordsActorAndTime()
    {
        var suggestion =
            Mapping("t1", "BC-1", CustomerMappingState.Suggested, CustomerMatchMethod.Cvr);

        var repository =
            await RepositoryWith(suggestion);

        var confirmed =
            await Service(repository).ConfirmAsync(
                suggestion.Id,
                "reviewer",
                "checked"
            );

        Assert.Equal(CustomerMappingState.Confirmed, confirmed.State);
        Assert.Equal(Now, confirmed.ConfirmedAt);
        Assert.Equal("reviewer", confirmed.ConfirmedBy);
        Assert.Equal("checked", confirmed.Notes);
        Assert.Equal(confirmed, await repository.GetAsync(suggestion.Id));
    }

    [Fact]
    public async Task RejectAndRetire_RecordAuditFields()
    {
        var suggestion =
            Mapping("t1", "BC-1", CustomerMappingState.Suggested);

        var active =
            Mapping("t2", "BC-2", CustomerMappingState.Confirmed);

        var repository =
            await RepositoryWith(suggestion, active);

        var service =
            Service(repository);

        var rejected =
            await service.RejectAsync(suggestion.Id, "reviewer");

        var retired =
            await service.RetireAsync(active.Id, "reviewer");

        Assert.Equal(CustomerMappingState.Rejected, rejected.State);
        Assert.Equal("reviewer", rejected.RejectedBy);
        Assert.Equal(Now, rejected.RejectedAt);

        Assert.Equal(CustomerMappingState.Retired, retired.State);
        Assert.Equal("reviewer", retired.RetiredBy);
        Assert.Equal(Now, retired.RetiredAt);

        Assert.Equal(2, (await repository.GetAllAsync()).Count);
    }

    [Theory]
    [InlineData("t1", "BC-OTHER")]
    [InlineData("T-OTHER", "bc-1 ")]
    public async Task Confirm_IsRefusedWhenEitherSideIsAlreadyConfirmed(
        string tenant,
        string customer)
    {
        var suggestion =
            Mapping(tenant, customer, CustomerMappingState.Suggested);

        var repository =
            await RepositoryWith(
                Mapping("t1", "BC-1", CustomerMappingState.Confirmed),
                suggestion
            );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(repository).ConfirmAsync(suggestion.Id, "reviewer")
        );

        Assert.Equal(
            CustomerMappingState.Suggested,
            (await repository.GetAsync(suggestion.Id))!.State
        );
    }

    [Fact]
    public async Task Confirm_AfterRetiringExistingLink_Succeeds()
    {
        var old =
            Mapping("t1", "BC-1", CustomerMappingState.Confirmed);

        var replacement =
            Mapping("t1", "BC-2", CustomerMappingState.Suggested);

        var service =
            Service(await RepositoryWith(old, replacement));

        await service.RetireAsync(old.Id, "reviewer");

        var confirmed =
            await service.ConfirmAsync(replacement.Id, "reviewer");

        Assert.Equal(CustomerMappingState.Confirmed, confirmed.State);
    }

    [Fact]
    public async Task Decisions_RequireAnActor()
    {
        var suggestion =
            Mapping("t1", "BC-1", CustomerMappingState.Suggested);

        var service =
            Service(await RepositoryWith(suggestion));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ConfirmAsync(suggestion.Id, " ")
        );
    }

    [Fact]
    public async Task UnknownMapping_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(new InMemoryCustomerMappingRepository())
                .ConfirmAsync(Guid.NewGuid(), "reviewer")
        );
    }

    [Fact]
    public async Task AddManual_CreatesConfirmedManualMapping()
    {
        var repository =
            await RepositoryWith(
                Mapping("t1", "BC-1", CustomerMappingState.Rejected)
            );

        var mapping =
            await Service(repository).AddManualAsync(
                " t1 ",
                "BC-1",
                "reviewer"
            );

        Assert.Equal("t1", mapping.AcronisTenantId);
        Assert.Equal(CustomerMappingState.Confirmed, mapping.State);
        Assert.Equal(CustomerMatchMethod.Manual, mapping.MatchMethod);
        Assert.Null(mapping.MatchScore);
        Assert.Equal("reviewer", mapping.ConfirmedBy);
    }

    [Fact]
    public async Task AddManual_IsRefusedForPendingSuggestionOrConfirmedConflict()
    {
        var service =
            Service(
                await RepositoryWith(
                    Mapping("t1", "BC-1", CustomerMappingState.Suggested),
                    Mapping("t2", "BC-2", CustomerMappingState.Confirmed)
                )
            );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddManualAsync("t1", "BC-1", "reviewer")
        );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddManualAsync("t3", "BC-2", "reviewer")
        );
    }

    [Fact]
    public async Task IdentitySource_ReturnsConfirmedMappingsOnly()
    {
        var repository =
            await RepositoryWith(
                Mapping("t1", "BC-1", CustomerMappingState.Confirmed),
                Mapping("t2", "BC-2", CustomerMappingState.Suggested),
                Mapping("t3", "BC-3", CustomerMappingState.Rejected),
                Mapping("t4", "BC-4", CustomerMappingState.Retired)
            );

        var identity =
            Assert.Single(
                await new ConfirmedMappingIdentitySource(repository)
                    .GetIdentitiesAsync()
            );

        Assert.Equal("BC-1", identity.BusinessCentralCustomerNumber);
        Assert.Equal("t1", identity.AcronisTenantId);
    }

    [Fact]
    public async Task IdentitySource_FailsClosedOnDuplicateConfirmedMappings()
    {
        var repository =
            await RepositoryWith(
                Mapping("t1", "BC-1", CustomerMappingState.Confirmed),
                Mapping("t2", "bc-1", CustomerMappingState.Confirmed)
            );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ConfirmedMappingIdentitySource(repository)
                .GetIdentitiesAsync()
        );
    }

    private static CustomerMappingConfirmationService Service(
        ICustomerMappingRepository repository)
    {
        return new CustomerMappingConfirmationService(
            repository,
            new FixedTimeProvider()
        );
    }
}
