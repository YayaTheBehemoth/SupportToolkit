using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Composition;

/// <summary>
/// Bulk delete is built from the profile's deletion connection, resolved
/// on every call. Opening a session contacts nothing, and only a session
/// opened with deletion enabled can delete.
/// </summary>
public class ZendeskBulkDeleteCompositionTests
{
    [Fact]
    public async Task Availability_FollowsTheProfilesDeletionConnection()
    {
        var (_, store, resolver) = Create(FullConfiguration());

        var available =
            await ZendeskBulkDeleteComposition.GetAvailabilityAsync(
                resolver,
                CancellationToken.None);

        Assert.True(available.IsAvailable);
        Assert.Equal("example", available.Subdomain);

        store.Configuration.Profiles["work"].ZendeskDeletionConnection = null;

        var unavailable =
            await ZendeskBulkDeleteComposition.GetAvailabilityAsync(
                resolver,
                CancellationToken.None);

        Assert.False(unavailable.IsAvailable);
        Assert.Null(unavailable.Subdomain);
        Assert.False(string.IsNullOrWhiteSpace(unavailable.Reason));
    }

    [Fact]
    public async Task ASessionOpenedWithoutDeletion_CannotDelete()
    {
        var (_, _, resolver) = Create(FullConfiguration());

        using var session =
            await ZendeskBulkDeleteComposition.OpenSessionAsync(resolver);

        Assert.Null(session.Executor);
        Assert.False(session.CreateReviewSession().CanDelete);
        Assert.Equal("example", session.CreateReviewSession().Instance);
    }
}
