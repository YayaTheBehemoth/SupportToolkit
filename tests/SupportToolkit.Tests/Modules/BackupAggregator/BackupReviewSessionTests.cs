using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupAggregator.Ticketing;
using SupportToolkit.Modules.BackupAggregator.Production;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

/// <summary>
/// Preparing a ticket, the review session and availability are the
/// user-interface-agnostic rules behind the console menu: any interface
/// drives the same ones.
/// </summary>
public class BackupReviewSessionTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    // ---- Ticket preparation (the single gate) ----

    [Fact]
    public async Task NoReview_IsTypedNotReady_NotAnException()
    {
        var workflow = Workflow(withZendesk: true);

        var draft = workflow.PrepareScopedTicketDraft(null);

        Assert.False(draft.IsReady);
        Assert.Equal(TicketNotReadyKind.NoReview, draft.NotReady.Kind);
        Assert.NotEmpty(draft.NotReady.Reasons);

        var submission =
            await workflow.PrepareScopedTicketSubmissionAsync(null);

        Assert.False(submission.IsReady);
        Assert.Equal(TicketNotReadyKind.NoReview, submission.NotReady.Kind);
    }

    [Fact]
    public async Task AnIncompleteReview_IsNotReady_WithItsBlockersAsTheReasons()
    {
        var workflow = Workflow(withZendesk: true);
        var review = Incomplete(await CompleteReview());

        var draft = workflow.PrepareScopedTicketDraft(review);

        Assert.False(draft.IsReady);
        Assert.Equal(TicketNotReadyKind.ReviewIncomplete, draft.NotReady.Kind);
        Assert.Equal(review.TicketBlockers, draft.NotReady.Reasons);
        Assert.Null(draft.Value);
    }

    [Fact]
    public async Task ACompleteReview_PreparesTheSameDraftAsBefore()
    {
        var workflow = Workflow(withZendesk: true);
        var review = await CompleteReview();

        var draft = workflow.PrepareScopedTicketDraft(review);

        Assert.True(draft.IsReady);

        var legacy = workflow.CreateScopedTicketDraft(review);

        Assert.Equal(legacy.Subject, draft.Value.Subject);
        Assert.Equal(legacy.Body, draft.Value.Body);
    }

    [Fact]
    public async Task CreateScopedTicketDraft_StillThrowsTheSameMessage_ForAnIncompleteReview()
    {
        var workflow = Workflow(withZendesk: true);
        var review = Incomplete(await CompleteReview());

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => workflow.CreateScopedTicketDraft(review));

        Assert.StartsWith(
            "No ticket can be created because the monitored review is " +
            "not complete: ",
            exception.Message);

        Assert.Contains(review.TicketBlockers[0], exception.Message);
    }

    [Fact]
    public async Task APreview_NeedsNoZendeskConfiguration()
    {
        var workflow = Workflow(withZendesk: false);

        var draft =
            workflow.PrepareScopedTicketDraft(await CompleteReview());

        Assert.True(draft.IsReady);
    }

    [Fact]
    public async Task ASubmission_BundlesTheDraftTheDestinationAndTheReviewNumbers()
    {
        var workflow = Workflow(withZendesk: true);
        var review = await CompleteReview();

        var preparation =
            await workflow.PrepareScopedTicketSubmissionAsync(review);

        Assert.True(preparation.IsReady);

        var plan = preparation.Value;

        Assert.Equal("fixture-trial.zendesk.com", plan.Destination.InstanceHost);
        Assert.Equal("dev", plan.Destination.DestinationName);
        Assert.Equal(12345, plan.Destination.GroupId);

        Assert.Equal(review.Inventory.TenantCount, plan.Review.TenantCount);
        Assert.Equal(review.Inventory.FindingsCount, plan.Review.FindingsCount);
        Assert.True(plan.Review.IsComplete);

        Assert.Equal(
            workflow.CreateScopedTicketDraft(review).Body,
            plan.Draft.Body);
    }

    [Fact]
    public async Task AMissingDestination_IsTyped_AndNeverHidesAnIncompleteReview()
    {
        var workflow = Workflow(withZendesk: false);

        var complete =
            await workflow.PrepareScopedTicketSubmissionAsync(
                await CompleteReview());

        Assert.False(complete.IsReady);
        Assert.Equal(
            TicketNotReadyKind.DestinationUnavailable,
            complete.NotReady.Kind);

        // The review problem is reported first, with no Zendesk lookup.
        var incomplete =
            await workflow.PrepareScopedTicketSubmissionAsync(
                Incomplete(await CompleteReview()));

        Assert.Equal(
            TicketNotReadyKind.ReviewIncomplete,
            incomplete.NotReady!.Kind);
    }

    [Fact]
    public async Task ASubmission_FailsClosed_IfTheDestinationChangedSinceConfirmation()
    {
        var workflow = Workflow(withZendesk: true);

        var plan =
            (await workflow.PrepareScopedTicketSubmissionAsync(
                await CompleteReview())).Value!;

        var stale =
            plan with
            {
                Destination =
                    new SupportToolkit.Core.Configuration
                        .TicketDestinationPreview(
                            "different.zendesk.com",
                            "dev",
                            12345)
            };

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => workflow.SubmitPlannedTicketAsync(stale));

        Assert.Contains("changed since confirmation", exception.Message);
        Assert.Contains("No ticket was submitted", exception.Message);
    }

    [Fact]
    public async Task TheReviewSummary_IsDataNotDisplayText()
    {
        var review = await CompleteReview();

        var summary = ReviewSummary.From(review);

        Assert.Equal(review.Inventory.TenantCount, summary.TenantCount);
        Assert.Equal(review.Inventory.TenantsAttempted, summary.TenantsAttempted);
        Assert.Equal(review.Inventory.FindingsCount, summary.FindingsCount);
        Assert.Equal(review.Inventory.UnknownRowsCount, summary.UnclassifiedCount);
        Assert.True(summary.IsComplete);

        Assert.False(ReviewSummary.From(Incomplete(review)).IsComplete);
    }

    // ---- Availability ----

    [Fact]
    public async Task FixtureMode_IsAlwaysAvailable()
    {
        var scoped = Scoped(new SupportToolkitConfiguration());

        var availability = await scoped.GetAvailabilityAsync();

        Assert.Equal(SupportToolkitMode.Fixture, availability.Mode);
        Assert.True(availability.IsAvailable);
        Assert.Null(availability.Reason);
    }

    [Fact]
    public async Task Production_IsAvailableOnlyWithABusinessCentralConnection()
    {
        var withConnection = ProductionConfiguration();

        Assert.True(
            (await Scoped(withConnection).GetAvailabilityAsync()).IsAvailable);

        var without = ProductionConfiguration();
        without.Profiles["work"].BusinessCentralConnection = null;

        var availability = await Scoped(without).GetAvailabilityAsync();

        Assert.Equal(SupportToolkitMode.Production, availability.Mode);
        Assert.False(availability.IsAvailable);
        Assert.Contains("Business Central", availability.Reason);
    }

    // ---- The session ----

    [Fact]
    public async Task ANewSession_HasNoReview_AndNothingCanBePrepared()
    {
        var session = Session();

        Assert.Null(session.CurrentReview);
        Assert.Null(session.CurrentSummary);

        Assert.Equal(
            TicketNotReadyKind.NoReview,
            session.PreviewTicket().NotReady!.Kind);

        Assert.Equal(
            TicketNotReadyKind.NoReview,
            (await session.PrepareSubmissionAsync()).NotReady!.Kind);
    }

    [Fact]
    public async Task RunningTheReview_SetsTheCurrentReview_AndAllowsAPreview()
    {
        var session = Session();

        var review = await session.RunReviewAsync();

        Assert.Same(review, session.CurrentReview);
        Assert.NotNull(session.CurrentSummary);
        Assert.True(session.PreviewTicket().IsReady);
    }

    [Fact]
    public async Task ThePlan_IsOnlyValidForTheReviewItWasPreparedFrom()
    {
        var session = Session();

        await session.RunReviewAsync();

        var plan = (await session.PrepareSubmissionAsync()).Value!;

        // Running the review again discards the plan.
        await session.RunReviewAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.SubmitAsync(plan));

        Assert.Contains("no longer current", exception.Message);
    }

    [Fact]
    public async Task AForeignPlan_IsRefused_EvenIfItLooksIdentical()
    {
        var session = Session();

        await session.RunReviewAsync();

        var prepared = (await session.PrepareSubmissionAsync()).Value!;

        var copy = prepared with { };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SubmitAsync(copy));
    }

    [Fact]
    public async Task APlan_IsSingleUse_WhateverTheOutcome()
    {
        var configuration = ZendeskConfiguration();
        var session = Session(configuration);

        await session.RunReviewAsync();

        var plan = (await session.PrepareSubmissionAsync()).Value!;

        // Change the destination after confirmation, so the submit
        // fails closed locally (nothing is sent).
        configuration.Connections.Zendesk["trial"]
            .Destinations["dev"].GroupId = 99999;

        var first =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.SubmitAsync(plan));

        Assert.Contains("changed since confirmation", first.Message);

        // The same plan cannot be sent again without a new confirmation.
        var second =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.SubmitAsync(plan));

        Assert.Contains("no longer current", second.Message);
    }

    [Fact]
    public async Task PreparingAgain_ReplacesThePreviousPlan()
    {
        var session = Session();

        await session.RunReviewAsync();

        var first = (await session.PrepareSubmissionAsync()).Value!;
        var second = (await session.PrepareSubmissionAsync()).Value!;

        Assert.NotSame(first, second);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SubmitAsync(first));
    }

    [Fact]
    public async Task WithoutAScopedWorkflow_TheReviewIsUnavailable_AndCannotRun()
    {
        var session =
            new BackupReviewSession(
                BackupAggregatorComposition.Workflow(
                    Resolver(new SupportToolkitConfiguration())),
                null);

        var availability = await session.GetAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.Contains("not available in this host", availability.Reason);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RunReviewAsync());
    }

    [Fact]
    public async Task AFailedRefresh_LeavesNoCurrentReview_AsBefore()
    {
        var calls = 0;

        var scoped =
            BackupAggregatorComposition.ScopedWorkflow(
                Resolver(ProductionConfiguration()),
                new FixtureMonitoringIdentitySource(),
                _ =>
                {
                    if (++calls > 1)
                    {
                        throw new InvalidOperationException("boom");
                    }

                    return Task.FromResult<ISmartSubscriptionEvidenceSource>(
                        new FixtureSmartSubscriptionEvidenceSource());
                },
                _ =>
                {
                    var inventory = new HealthyFixtureInventoryProvider(Today);

                    return Task.FromResult(
                        new ScopedInventoryRuntime(
                            AcronisBackupInventory.CreateReviewService(
                                new AcronisTenantResolver(
                                    new FixtureCustomerTenantProvider()),
                                inventory,
                                inventory,
                                new BackupInventoryNormalizer()),
                            null));
                });

        var session =
            new BackupReviewSession(
                BackupAggregatorComposition.Workflow(
                    Resolver(ProductionConfiguration())),
                scoped);

        await session.RunReviewAsync();

        Assert.NotNull(session.CurrentReview);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RunReviewAsync());

        Assert.Null(session.CurrentReview);
        Assert.Null(session.CurrentSummary);
        Assert.Equal(
            TicketNotReadyKind.NoReview,
            session.PreviewTicket().NotReady!.Kind);
    }


    // ---- Helpers ----

    private static async Task<ScopedBackupInventoryReviewResult>
        CompleteReview() =>
        await Scoped(new SupportToolkitConfiguration()).ReviewAsync(Today);

    /// <summary>
    /// The same review with one unresolved notice, which blocks a ticket.
    /// </summary>
    private static ScopedBackupInventoryReviewResult Incomplete(
        ScopedBackupInventoryReviewResult review) =>
        review with
        {
            Selection =
                review.Selection with
                {
                    Notices = ["A notice that blocks the ticket."]
                }
        };

    private static BackupReviewSession Session(
        SupportToolkitConfiguration? configuration = null)
    {
        configuration ??= ZendeskConfiguration();

        var resolver = Resolver(configuration);

        return new BackupReviewSession(
            BackupAggregatorComposition.Workflow(resolver),
            BackupAggregatorComposition.ScopedWorkflow(resolver));
    }

    private static BackupAggregatorWorkflow Workflow(
        bool withZendesk) =>
        BackupAggregatorComposition.Workflow(
            Resolver(
                withZendesk
                    ? ZendeskConfiguration()
                    : new SupportToolkitConfiguration()));

    private static BackupAggregatorScopedWorkflow Scoped(
        SupportToolkitConfiguration configuration) =>
        BackupAggregatorComposition.ScopedWorkflow(Resolver(configuration));

    private static SupportToolkitConfiguration ZendeskConfiguration()
    {
        var configuration =
            new SupportToolkitConfiguration
            {
                ActiveProfile = "work",
                Profiles =
                {
                    ["work"] =
                        new SupportToolkitProfile
                        {
                            ZendeskConnection = "trial",
                            ZendeskDestination = "dev"
                        }
                }
            };

        configuration.Connections.Zendesk["trial"] =
            new ZendeskConnectionConfiguration
            {
                Subdomain = "fixture-trial",
                ClientId = "fixture-client",
                SubmitterId = 900000000001,
                RequesterId = 900000000002,
                Destinations =
                {
                    ["dev"] =
                        new ZendeskDestinationConfiguration
                        {
                            GroupId = 12345
                        }
                }
            };

        return configuration;
    }

    private static SupportToolkitConfiguration ProductionConfiguration() =>
        new()
        {
            ActiveProfile = "work",
            Profiles =
            {
                ["work"] =
                    new SupportToolkitProfile
                    {
                        BusinessCentralConnection = "bc-work",
                        Modules =
                        {
                            ["backup-aggregator"] =
                                SupportToolkitMode.Production
                        },
                        MonitoringScope =
                        {
                            ["backup-aggregator"] =
                                new ModuleMonitoringScopeConfiguration
                                {
                                    MonitoringItemNumber =
                                        SmartSubscriptionFixtureFactory
                                            .FixtureMonitoringItemNumber
                                }
                        }
                    }
            }
        };

    private static SupportToolkitConfigurationResolver Resolver(
        SupportToolkitConfiguration configuration) =>
        new(
            new Store(configuration),
            new Secrets(),
            _ => null);

    private sealed class Store(SupportToolkitConfiguration configuration)
        : ISupportToolkitConfigurationStore
    {
        public string ConfigurationPath => "test-config.json";

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(configuration);

        public Task SaveAsync(
            SupportToolkitConfiguration value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class Secrets : ISecretStore
    {
        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("fixture-secret");

        public Task SetSecretAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
