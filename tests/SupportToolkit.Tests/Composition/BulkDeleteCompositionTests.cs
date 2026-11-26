using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.BulkDelete.Production;
using SupportToolkit.Providers.OsTicket.Configuration;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Composition;

/// <summary>
/// Bulk delete is built from the profile's deletion connection, resolved
/// on every call. Opening a session contacts nothing, and only a session
/// opened with deletion enabled can delete.
/// </summary>
public class BulkDeleteCompositionTests
{
    [Fact]
    public async Task Availability_FollowsTheProfilesDeletionConnection()
    {
        var (_, store, resolver) = Create(FullConfiguration());

        var available =
            await BulkDeleteComposition.GetAvailabilityAsync(
                resolver,
                CancellationToken.None);

        Assert.True(available.IsAvailable);
        Assert.Equal("example", available.Instance);

        store.Configuration.Profiles["work"].ZendeskDeletionConnection = null;

        var unavailable =
            await BulkDeleteComposition.GetAvailabilityAsync(
                resolver,
                CancellationToken.None);

        Assert.False(unavailable.IsAvailable);
        Assert.Null(unavailable.Instance);
        Assert.False(string.IsNullOrWhiteSpace(unavailable.Reason));
    }

    [Fact]
    public async Task ASessionOpenedWithoutDeletion_CannotDelete()
    {
        var (_, _, resolver) = Create(FullConfiguration());

        using var session =
            await BulkDeleteComposition.OpenSessionAsync(resolver);

        Assert.Null(session.Executor);
        Assert.False(session.CreateReviewSession().CanDelete);
        Assert.Equal("example", session.CreateReviewSession().Instance);
    }

    [Fact]
    public async Task AZendeskProfile_DeletesInZendesk_WithZendesksRules()
    {
        var (_, _, resolver) = Create(FullConfiguration());

        var availability =
            await BulkDeleteComposition.GetAvailabilityAsync(
                resolver,
                CancellationToken.None);

        Assert.True(availability.IsAvailable);
        Assert.Equal("Zendesk", availability.SystemName);
        Assert.Same(ZendeskDeletionRules.Rules, availability.Rules);
        Assert.Equal("example.zendesk.com", availability.DisplayInstance);
    }

    [Fact]
    public async Task AProfileWhoseTicketsGoElsewhere_HasNoBulkDelete_EvenWithAZendeskDeletionConnection()
    {
        var (_, store, resolver) = Create(FullConfiguration());

        store.Configuration.Profiles["work"].Ticketing =
            new TicketingSelectionConfiguration
            {
                Provider = OsTicketConnections.ProviderKey,
                Connection = "lab"
            };

        var availability =
            await BulkDeleteComposition.GetAvailabilityAsync(
                resolver,
                CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Equal("osTicket", availability.SystemName);
        Assert.Null(availability.Rules);
        Assert.Contains("cannot delete tickets in osTicket", availability.Reason);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BulkDeleteComposition.ConnectWithDeletionAsync(
                resolver,
                null,
                CancellationToken.None));

        var console =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BulkDeleteComposition.OpenSessionAsync(resolver));

        Assert.Contains("osTicket", console.Message);
    }

    [Fact]
    public async Task ADeletionConnectionForAnotherInstance_IsRefused()
    {
        var (_, store, resolver) = Create(FullConfiguration());

        store.Configuration.Connections.ZendeskDeletion["del"].Subdomain = "other";

        var availability =
            await BulkDeleteComposition.GetAvailabilityAsync(
                resolver,
                CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Contains("example.zendesk.com", availability.Reason);
        Assert.Contains("other.zendesk.com", availability.Reason);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BulkDeleteComposition.OpenSessionAsync(resolver));
    }

    [Fact]
    public async Task AProfileWithoutTicketing_KeepsUsingItsZendeskDeletionConnection()
    {
        var (_, store, resolver) = Create(FullConfiguration());

        store.Configuration.Profiles["work"].ZendeskConnection = null;
        store.Configuration.Profiles["work"].ZendeskDestination = null;
        store.Configuration.Connections.ZendeskDeletion["del"].Subdomain = "other";

        var availability =
            await BulkDeleteComposition.GetAvailabilityAsync(
                resolver,
                CancellationToken.None);

        Assert.True(availability.IsAvailable);
        Assert.Equal("Zendesk", availability.SystemName);
        Assert.Equal("other", availability.Instance);
    }
}
