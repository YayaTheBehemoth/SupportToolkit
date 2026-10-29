using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupAggregator.Ticketing;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

/// <summary>
/// The review waits for resources that are still running, re-reviews
/// only the tenants that have some, and never reports an unfinished
/// resource as healthy. No test sleeps: the clock is a fake whose
/// delays complete at once and advance its time.
/// </summary>
public class InProgressWaitTests
{
    private const string TenantA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string TenantB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ResolvesAfterTwoPolls_AndReReviewsOnlyTheAffectedTenant()
    {
        var devices =
            new ScriptedDevices(
                (TenantA, call => call <= 2 ? Running() : Finished()),
                (TenantB, _ => Finished()));

        var clock = new FakeClock();
        var messages = new List<string>();

        var report =
            await CreateService(
                    devices,
                    new InProgressWaitOptions(
                        TimeSpan.FromMinutes(10),
                        Poll),
                    clock,
                    messages.Add)
                .ReviewAllTenantsAsync();

        Assert.Equal(3, devices.CallsFor(TenantA));
        Assert.Equal(1, devices.CallsFor(TenantB));

        Assert.Equal(0, report.InProgressCount);
        Assert.True(report.IsTenantReviewComplete);

        var wait = Assert.IsType<InProgressWaitSummary>(
            report.InProgressWait);

        Assert.Equal(2, wait.Polls);
        Assert.Equal(0, wait.PendingResources);
        Assert.False(wait.TimedOut);
        Assert.Equal(TimeSpan.FromSeconds(60), wait.Elapsed);

        Assert.Contains(
            messages,
            message => message.StartsWith(
                "Waiting for in-progress backups: 1 resource(s) in 1 " +
                "tenant(s)",
                StringComparison.Ordinal));

        Assert.Contains(
            messages,
            message => message.Contains("finished after"));
    }

    [Fact]
    public async Task NeverResolves_StopsAtTheLimit_AndStaysInProgress()
    {
        var devices =
            new ScriptedDevices(
                (TenantA, _ => Running()),
                (TenantB, _ => Finished()));

        var clock = new FakeClock();

        var report =
            await CreateService(
                    devices,
                    new InProgressWaitOptions(
                        TimeSpan.FromSeconds(100),
                        Poll),
                    clock)
                .ReviewAllTenantsAsync();

        var wait = Assert.IsType<InProgressWaitSummary>(
            report.InProgressWait);

        Assert.True(wait.TimedOut);
        Assert.Equal(1, wait.PendingResources);
        Assert.Equal(1, wait.PendingTenants);
        Assert.Equal(TimeSpan.FromSeconds(100), wait.Elapsed);

        // 30 + 30 + 30 + 10 seconds: the last delay is cut to the limit.
        Assert.Equal(4, wait.Polls);
        Assert.Equal(5, devices.CallsFor(TenantA));
        Assert.Equal(1, devices.CallsFor(TenantB));

        // Never reported as healthy: still a visible In progress finding.
        var finding =
            Assert.Single(
                report.Tenants.Single(t => t.TenantName == "Tenant A")
                    .Findings);

        Assert.True(finding.IsInProgress);
        Assert.Equal(1, report.InProgressCount);

        // The run itself is still complete; the ticket stays possible
        // and says plainly that the result is not final.
        Assert.True(report.IsTenantReviewComplete);

        var draft = new BackupReviewTicketDraftFactory().Create(report);

        Assert.Contains("Status: In progress", draft.Body);
        Assert.Contains("Still in progress after waiting", draft.Body);
        Assert.Contains("not final", draft.Body);
    }

    [Fact]
    public async Task NothingInProgress_MakesNoDelayAndNoExtraCalls()
    {
        var devices =
            new ScriptedDevices(
                (TenantA, _ => Finished()),
                (TenantB, _ => Finished()));

        var clock = new FakeClock();
        var messages = new List<string>();

        var report =
            await CreateService(
                    devices,
                    new InProgressWaitOptions(
                        TimeSpan.FromMinutes(10),
                        Poll),
                    clock,
                    messages.Add)
                .ReviewAllTenantsAsync();

        Assert.Empty(clock.Delays);
        Assert.Equal(1, devices.CallsFor(TenantA));
        Assert.Equal(1, devices.CallsFor(TenantB));
        Assert.Null(report.InProgressWait);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task ReReviewFailure_IsAFailedTenantNeverASuccess()
    {
        var devices =
            new ScriptedDevices(
                (TenantA,
                    call => call == 1
                        ? Running()
                        : throw new HttpRequestException(
                            "boom",
                            null,
                            System.Net.HttpStatusCode.BadGateway)),
                (TenantB, _ => Finished()));

        var report =
            await CreateService(
                    devices,
                    new InProgressWaitOptions(
                        TimeSpan.FromMinutes(10),
                        Poll),
                    new FakeClock())
                .ReviewAllTenantsAsync();

        var failure = Assert.Single(report.Failures);
        Assert.Equal("Tenant A", failure.TenantName);
        Assert.Contains("HTTP 502", failure.Reason);

        Assert.DoesNotContain(
            report.Tenants,
            tenant => tenant.TenantName == "Tenant A");

        Assert.False(report.IsTenantReviewComplete);
        Assert.Equal(2, devices.CallsFor(TenantA));

        Assert.Throws<InvalidOperationException>(
            () => new BackupReviewTicketDraftFactory().Create(report));
    }

    [Fact]
    public async Task WaitDisabled_ReviewsOnce_AndLeavesTheResourceInProgress()
    {
        var devices =
            new ScriptedDevices(
                (TenantA, _ => Running()),
                (TenantB, _ => Finished()));

        var clock = new FakeClock();

        var report =
            await CreateService(
                    devices,
                    InProgressWaitOptions.Disabled,
                    clock)
                .ReviewAllTenantsAsync();

        Assert.Equal(1, devices.CallsFor(TenantA));
        Assert.Empty(clock.Delays);
        Assert.Null(report.InProgressWait);
        Assert.Equal(1, report.InProgressCount);
    }

    [Fact]
    public async Task ARunningDeviceWithAPriorSuccess_IsAlsoWaitedFor()
    {
        var devices =
            new ScriptedDevices(
                (TenantA,
                    call => call == 1
                        ? Running(withPriorSuccess: true)
                        : Finished()),
                (TenantB, _ => Finished()));

        var report =
            await CreateService(
                    devices,
                    new InProgressWaitOptions(
                        TimeSpan.FromMinutes(10),
                        Poll),
                    new FakeClock())
                .ReviewAllTenantsAsync();

        Assert.Equal(2, devices.CallsFor(TenantA));
        Assert.Equal(0, report.InProgressCount);
    }

    [Fact]
    public async Task PendingWithNoFindings_IsNeverASilentlyCleanSubject()
    {
        var devices =
            new ScriptedDevices(
                (TenantA, _ => Running(withPriorSuccess: true)),
                (TenantB, _ => Finished()));

        var report =
            await CreateService(
                    devices,
                    new InProgressWaitOptions(
                        TimeSpan.FromSeconds(30),
                        Poll),
                    new FakeClock())
                .ReviewAllTenantsAsync();

        // Healthy while running, so it is hidden from the findings,
        // yet it is still pending and the ticket must say so.
        Assert.Equal(0, report.FindingsCount);
        Assert.Equal(1, report.InProgressCount);

        var draft = new BackupReviewTicketDraftFactory().Create(report);

        Assert.DoesNotContain("no attention required", draft.Subject);
        Assert.Contains("still in progress", draft.Subject);
    }

    [Theory]
    [InlineData(null, null, 1800, 30)]
    [InlineData(600, null, 600, 30)]
    [InlineData(0, null, 0, 0)]
    [InlineData(0, 999, 0, 0)]
    [InlineData(120, 120, 120, 120)]
    public void Settings_AcceptValidValuesAndApplyDefaults(
        int? max,
        int? poll,
        int expectedMax,
        int expectedPoll)
    {
        var settings = InProgressWaitSettings.Create(max, poll);

        Assert.Equal(expectedMax, settings.MaxWaitSeconds);
        Assert.Equal(expectedPoll, settings.PollIntervalSeconds);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(86401, null)]
    [InlineData(60, 0)]
    [InlineData(60, 61)]
    public void Settings_RejectInvalidValues(int max, int? poll)
    {
        Assert.Throws<InvalidOperationException>(
            () => InProgressWaitSettings.Create(max, poll));
    }

    [Fact]
    public void Options_ZeroDisablesAndNegativeIsRejected()
    {
        Assert.False(
            InProgressWaitOptions.Create(
                    TimeSpan.Zero,
                    TimeSpan.Zero)
                .Enabled);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => InProgressWaitOptions.Create(
                TimeSpan.FromSeconds(-1),
                Poll));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => InProgressWaitOptions.Create(
                TimeSpan.FromMinutes(1),
                TimeSpan.Zero));
    }

    [Fact]
    public async Task Resolver_UsesDefaultsWithoutASetting_AndReadsTheProfileSetting()
    {
        var configuration =
            new SupportToolkitConfiguration
            {
                ActiveProfile = "work",
                Profiles =
                {
                    ["work"] = new SupportToolkitProfile()
                }
            };

        var resolver = CreateResolver(configuration);

        Assert.Equal(
            InProgressWaitSettings.Default,
            await resolver.GetInProgressWaitSettingsAsync(
                "backup-aggregator"));

        configuration.Profiles["work"].Review["backup-aggregator"] =
            new ModuleReviewConfiguration
            {
                InProgressMaxWaitSeconds = 90,
                InProgressPollIntervalSeconds = 15
            };

        Assert.Equal(
            new InProgressWaitSettings(90, 15),
            await resolver.GetInProgressWaitSettingsAsync(
                "backup-aggregator"));

        configuration.Profiles["work"].Review["backup-aggregator"] =
            new ModuleReviewConfiguration
            {
                InProgressMaxWaitSeconds = -5
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.GetInProgressWaitSettingsAsync(
                "backup-aggregator"));
    }

    [Fact]
    public async Task Resolver_UsesDefaultsWhenNoProfileExists()
    {
        var resolver =
            CreateResolver(new SupportToolkitConfiguration());

        Assert.Equal(
            InProgressWaitSettings.Default,
            await resolver.GetInProgressWaitSettingsAsync(
                "backup-aggregator"));
    }

    private static BackupInventoryReviewService CreateService(
        ScriptedDevices devices,
        InProgressWaitOptions options,
        TimeProvider clock,
        Action<string>? progress = null)
    {
        TenantDto[] tenants =
        [
            new() { Id = TenantA, Name = "Tenant A", Kind = "customer" },
            new() { Id = TenantB, Name = "Tenant B", Kind = "customer" }
        ];

        return new BackupInventoryReviewService(
            new AcronisTenantResolver(new StubTenants(tenants)),
            new NoMicrosoft365(),
            devices,
            new BackupInventoryNormalizer(),
            inProgressWait: options,
            timeProvider: clock,
            progress: progress);
    }

    private static SupportToolkitConfigurationResolver CreateResolver(
        SupportToolkitConfiguration configuration) =>
        new(new Store(configuration), new NoSecrets(), _ => null);

    private static AcronisDeviceResourceDto Running(
        bool withPriorSuccess = false) =>
        Device(
            "running",
            lastSuccess: withPriorSuccess
                ? DateTimeOffset.Parse("2026-10-14T01:00:00Z")
                : null);

    private static AcronisDeviceResourceDto Finished() =>
        Device(
            "idle",
            lastSuccess: DateTimeOffset.Parse("2026-10-15T01:00:00Z"));

    private static AcronisDeviceResourceDto Device(
        string state,
        DateTimeOffset? lastSuccess) =>
        new()
        {
            Id = "device-1",
            Name = "SERVER-01",
            Status =
                new AcronisDeviceResourceStatusDto
                {
                    State = state,
                    LastBackup = lastSuccess,
                    LastSuccessBackup = lastSuccess,
                    NextBackup =
                        DateTimeOffset.Parse("2026-10-16T01:00:00Z")
                }
        };

    private sealed class ScriptedDevices(
        params (string Tenant,
            Func<int, AcronisDeviceResourceDto> Resource)[] script)
        : IAcronisDeviceInventoryProvider
    {
        private readonly Dictionary<string, int> _calls =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new();

        public int CallsFor(string tenantId)
        {
            lock (_lock)
            {
                return _calls.GetValueOrDefault(tenantId);
            }
        }

        public Task<IReadOnlyList<AcronisDeviceResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            int call;

            lock (_lock)
            {
                call = _calls.GetValueOrDefault(tenantId) + 1;
                _calls[tenantId] = call;
            }

            var resource =
                script.Single(
                    entry => string.Equals(
                        entry.Tenant,
                        tenantId,
                        StringComparison.OrdinalIgnoreCase))
                    .Resource(call);

            return Task.FromResult<IReadOnlyList<AcronisDeviceResourceDto>>(
                [resource]);
        }
    }

    private sealed class NoMicrosoft365
        : IAcronisMicrosoft365InventoryProvider
    {
        public Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AcronisMicrosoft365ResourceDto>>(
                []);
    }

    private sealed class StubTenants(IReadOnlyList<TenantDto> tenants)
        : IAcronisTenantProvider
    {
        public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(tenants);
    }

    /// <summary>
    /// A clock whose delays complete immediately and move its time
    /// forward by the requested amount.
    /// </summary>
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now =
            new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

        public List<TimeSpan> Delays { get; } = [];

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            Delays.Add(dueTime);
            _now += dueTime;

            ThreadPool.QueueUserWorkItem(_ => callback(state));

            return new NoopTimer();
        }

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Store(SupportToolkitConfiguration configuration)
        : ISupportToolkitConfigurationStore
    {
        public string ConfigurationPath => "test-config.json";

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(configuration);

        public Task SaveAsync(
            SupportToolkitConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoSecrets : ISecretStore
    {
        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

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
