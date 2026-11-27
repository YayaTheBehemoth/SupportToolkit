using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.BackupAggregator.Production;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.UsageReport.Production;
using SupportToolkit.Modules.UsageReport.Services;
using SupportToolkit.Providers.Duplicati.Configuration;

namespace SupportToolkit.Composition;

/// <summary>
/// Chooses the backup system the active profile's Backup Aggregator
/// reviews. This is the only place that maps a backup system name to its
/// review; adding a system is one entry in Systems. The selection is read
/// on every review, so a profile switched while the program runs takes
/// effect on the next one.
/// </summary>
public static class BackupSystemsComposition
{
    private delegate Task<ScopedInventoryRuntime> RuntimeFactory(
        SupportToolkitConfigurationResolver resolver,
        ReviewRunMode runMode,
        Action<string>? progress,
        CancellationToken cancellationToken);

    private delegate Task<IReadOnlyList<StatusDetail>> Describer(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken);

    /// <param name="Describe">
    /// Resolves the selection locally, without calling the system, and
    /// returns its non-secret settings for status. Null for Acronis, whose
    /// status has its own section because other features read it too.
    /// </param>
    /// <param name="CheckUsage">
    /// The usage report's local check of the system's configuration;
    /// with OpenUsage, null when the usage report cannot read the system.
    /// </param>
    private sealed record BackupSystem(
        string Key,
        string Name,
        RuntimeFactory CreateRuntime,
        Describer? Describe = null,
        Func<SupportToolkitConfigurationResolver, CancellationToken, Task>? CheckUsage = null,
        Func<SupportToolkitConfigurationResolver, CancellationToken, Task<IUsageBackupSource>>? OpenUsage = null,
        string? UsageGbNote = null,
        bool UsageShowsTerabytes = false);

    private static readonly BackupSystem[] Systems =
    [
        new(
            BackupSelection.Providers.Acronis,
            "Acronis",
            CreateAcronisRuntimeAsync,
            CheckUsage: async (resolver, token) => await resolver.GetAcronisConnectionAsync(token),
            OpenUsage: AcronisUsageSource.CreateAsync,
            UsageGbNote: ProductionUsageReportSources.FirstSystemNames.GbNote,
            UsageShowsTerabytes: true),
        new(
            DuplicatiConnections.ProviderKey,
            "Duplicati",
            DuplicatiBackupAggregator.CreateRuntimeAsync,
            DescribeDuplicatiAsync,
            CheckUsage: async (resolver, token) =>
                await DuplicatiConnections.Instance.ResolveAsync(resolver, token),
            OpenUsage: DuplicatiUsageSource.CreateAsync)
    ];

    /// <summary>
    /// The usage report's backup half, from the same selection the review
    /// uses. A profile that selects no backup system reads Acronis, as the
    /// usage report always has. Both are resolved on every call.
    /// </summary>
    public static (Func<CancellationToken, Task> Check,
        Func<CancellationToken, Task<IUsageBackupSource>> Open,
        Func<CancellationToken, Task<(string Name, string? GbNote, bool ShowsTerabytes)>> Name)
        ForUsageReport(SupportToolkitConfigurationResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        async Task<BackupSystem> SelectAsync(CancellationToken token)
        {
            var selection = await resolver.GetBackupSelectionAsync(token);

            var system =
                selection is null
                    ? Systems.Single(candidate => candidate.Key == BackupSelection.Providers.Acronis)
                    : Find(selection)
                      ?? throw new InvalidOperationException(
                          selection.Provider.Length == 0
                              ? "The active profile's backup setting names no provider. " +
                                $"Use one of: {string.Join(", ", KnownProviders)}."
                              : $"The active profile selects backup provider " +
                                $"'{selection.Provider}', which SupportToolkit does not know. " +
                                $"Use one of: {string.Join(", ", KnownProviders)}.");

            return system.CheckUsage is null || system.OpenUsage is null
                ? throw new InvalidOperationException(
                    $"The usage report cannot read storage from {system.Name}.")
                : system;
        }

        return (
            async token =>
            {
                var system = await SelectAsync(token);
                await system.CheckUsage!(resolver, token);
            },
            async token =>
            {
                var system = await SelectAsync(token);
                return await system.OpenUsage!(resolver, token);
            },
            async token =>
            {
                var system = await SelectAsync(token);
                return (system.Name, system.UsageGbNote, system.UsageShowsTerabytes);
            });
    }

    public static IReadOnlyList<string> KnownProviders { get; } =
        Systems.Select(system => system.Key).ToArray();

    /// <summary>
    /// The selected system's name for display ("Duplicati"), or the
    /// configured value when no such system is registered.
    /// </summary>
    public static string SystemName(BackupSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return Find(selection)?.Name
            ?? (selection.Provider.Length == 0 ? "Backup" : selection.Provider);
    }

    public static async Task<ScopedInventoryRuntime> CreateRuntimeAsync(
        SupportToolkitConfigurationResolver resolver,
        ReviewRunMode runMode,
        Action<string>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var selection =
            await resolver.GetBackupSelectionAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The active profile selects no backup system. Configure an " +
                "Acronis connection with SupportToolkit configure profile " +
                "<name>, or select a system in the profile's backup setting.");

        if (selection.Provider.Length == 0)
        {
            throw new InvalidOperationException(
                "The active profile's backup setting names no provider. " +
                $"Use one of: {string.Join(", ", KnownProviders)}.");
        }

        var system =
            Find(selection)
            ?? throw new InvalidOperationException(
                $"The active profile selects backup provider " +
                $"'{selection.Provider}', which SupportToolkit does not know. " +
                $"Use one of: {string.Join(", ", KnownProviders)}.");

        return await system.CreateRuntime(resolver, runMode, progress, cancellationToken);
    }

    /// <summary>
    /// Acronis reads the profile's acronisConnection, which other Acronis
    /// reads (the usage report) share. A backup setting may name Acronis,
    /// but not a different Acronis connection.
    /// </summary>
    private static async Task<ScopedInventoryRuntime> CreateAcronisRuntimeAsync(
        SupportToolkitConfigurationResolver resolver,
        ReviewRunMode runMode,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var active = await resolver.GetActiveConfigurationAsync(cancellationToken);

        if (active.Profile.Backup?.Connection is { Length: > 0 } named
            && !string.Equals(
                named.Trim(),
                active.Profile.AcronisConnection?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.ProfileName}' names Acronis " +
                $"connection '{named.Trim()}' in its backup setting, but its " +
                "acronisConnection is different. Set acronisConnection " +
                "instead; the backup setting may then leave the connection out.");
        }

        return await BackupAggregatorProduction.CreateRuntimeAsync(
            resolver,
            runMode,
            progress,
            cancellationToken);
    }

    /// <summary>
    /// Whether the selection is Acronis, the only system with its own
    /// status section. True when nothing is selected, so status keeps
    /// asking for the Acronis connection a production review needs.
    /// </summary>
    public static bool ReviewsAcronis(BackupSelection? selection) =>
        selection is null || selection.Is(BackupSelection.Providers.Acronis);

    /// <summary>
    /// The selected non-Acronis system's settings, resolved locally
    /// without calling it. Fails with the reason when they are incomplete.
    /// </summary>
    public static async Task<IReadOnlyList<StatusDetail>> DescribeAsync(
        SupportToolkitConfigurationResolver resolver,
        BackupSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(selection);

        var system =
            Find(selection)
            ?? throw new InvalidOperationException(
                selection.Provider.Length == 0
                    ? "The active profile's backup setting names no provider. " +
                      $"Use one of: {string.Join(", ", KnownProviders)}."
                    : $"The active profile selects backup provider " +
                      $"'{selection.Provider}', which SupportToolkit does not know. " +
                      $"Use one of: {string.Join(", ", KnownProviders)}.");

        return system.Describe is null
            ? []
            : await system.Describe(resolver, cancellationToken);
    }

    private static async Task<IReadOnlyList<StatusDetail>> DescribeDuplicatiAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        var fleet =
            await DuplicatiConnections.Instance.ResolveAsync(resolver, cancellationToken);

        return
        [
            new StatusDetail("Customers", fleet.Customers.Count.ToString()),
            new StatusDetail(
                "Servers",
                fleet.Customers.Sum(customer => customer.Servers.Count).ToString()),
            new StatusDetail("Server passwords", "all configured")
        ];
    }

    private static BackupSystem? Find(BackupSelection selection) =>
        Systems.FirstOrDefault(system => selection.Is(system.Key));
}
