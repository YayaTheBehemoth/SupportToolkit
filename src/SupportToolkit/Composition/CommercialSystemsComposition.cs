using SupportToolkit.Core.Configuration;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Production;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Dolibarr.Configuration;
using SupportToolkit.Providers.Dolibarr.Subscriptions;

namespace SupportToolkit.Composition;

/// <summary>
/// Chooses the commercial system that supplies the monitored review's
/// entitlement evidence and customer names. This is the only place that
/// maps a commercial system name to its sources; adding a system is one
/// entry in Systems. The selection is read on every call, so a profile
/// switched while the program runs takes effect on the next review.
/// </summary>
public static class CommercialSystemsComposition
{
    /// <summary>The sources one workflow uses, chosen per call.</summary>
    public sealed record CommercialSources(
        Func<CancellationToken, Task<ISmartSubscriptionEvidenceSource>> Evidence,
        Func<CancellationToken, Task<ICustomerNameSource>> Names);

    private sealed record CommercialSystem(
        string Key,
        string Name,
        Func<SupportToolkitConfigurationResolver, ScopedReviewProductionSources, CancellationToken, Task<ISmartSubscriptionEvidenceSource>> Evidence,
        Func<SupportToolkitConfigurationResolver, ScopedReviewProductionSources, CancellationToken, Task<ICustomerNameSource>> Names);

    private static readonly CommercialSystem[] Systems =
    [
        new(
            CommercialSelection.Providers.BusinessCentral,
            "Business Central",
            async (resolver, businessCentral, token) =>
            {
                await RequireBusinessCentralFieldAsync(resolver, token);
                return await businessCentral.CreateEvidenceSourceAsync(token);
            },
            async (resolver, businessCentral, token) =>
            {
                await RequireBusinessCentralFieldAsync(resolver, token);
                return await businessCentral.CreateNameSourceAsync(token);
            }),
        new(
            DolibarrConnections.ProviderKey,
            DolibarrConnections.Instance.ProviderName,
            async (resolver, _, token) =>
                new DolibarrSubscriptionEvidenceSource(
                    await DolibarrConnections.Instance.ResolveAsync(resolver, token)),
            async (resolver, _, token) =>
                new DolibarrCustomerNameSource(
                    await DolibarrConnections.Instance.ResolveAsync(resolver, token)))
    ];

    public static IReadOnlyList<string> KnownProviders { get; } =
        Systems.Select(system => system.Key).ToArray();

    public static string SystemName(CommercialSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return Find(selection)?.Name
            ?? (selection.Provider.Length == 0 ? "Commercial" : selection.Provider);
    }

    /// <summary>
    /// The sources for one workflow. Business Central's evidence and names
    /// share one sign-in within it, as before.
    /// </summary>
    public static CommercialSources ForWorkflow(
        SupportToolkitConfigurationResolver resolver,
        ReviewRunMode runMode,
        Action<string>? progress)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var businessCentral = new ScopedReviewProductionSources(resolver, runMode, progress);

        return new CommercialSources(
            async token => await (await SystemAsync(resolver, token)).Evidence(resolver, businessCentral, token),
            async token => await (await SystemAsync(resolver, token)).Names(resolver, businessCentral, token));
    }

    /// <summary>
    /// Whether the selection is Business Central, the only system with its
    /// own status section. True when nothing is selected.
    /// </summary>
    public static bool ReadsBusinessCentral(CommercialSelection? selection) =>
        selection is null || selection.Is(CommercialSelection.Providers.BusinessCentral);

    /// <summary>
    /// The selected non-Business Central system's settings, resolved
    /// locally without calling it.
    /// </summary>
    public static async Task<IReadOnlyList<StatusDetail>> DescribeAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken = default)
    {
        var system = await SystemAsync(resolver, cancellationToken);

        if (system.Key == DolibarrConnections.ProviderKey)
        {
            var connection =
                await DolibarrConnections.Instance.ResolveAsync(resolver, cancellationToken);

            return
            [
                new StatusDetail("API", connection.ApiRoot.GetLeftPart(UriPartial.Path)),
                new StatusDetail("API key", "configured")
            ];
        }

        return [];
    }

    private static async Task<CommercialSystem> SystemAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        var selection =
            await resolver.GetCommercialSelectionAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The active profile selects no commercial system. Configure a " +
                "Business Central connection, or select a system in the " +
                "profile's commercial setting.");

        if (selection.Provider.Length == 0)
        {
            throw new InvalidOperationException(
                "The active profile's commercial setting names no provider. " +
                $"Use one of: {string.Join(", ", KnownProviders)}.");
        }

        return Find(selection)
            ?? throw new InvalidOperationException(
                $"The active profile selects commercial provider " +
                $"'{selection.Provider}', which SupportToolkit does not know. " +
                $"Use one of: {string.Join(", ", KnownProviders)}.");
    }

    /// <summary>
    /// Business Central reads the profile's businessCentralConnection, which
    /// the usage report shares. A commercial setting may name Business
    /// Central, but not a different connection.
    /// </summary>
    private static async Task RequireBusinessCentralFieldAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        var active = await resolver.GetActiveConfigurationAsync(cancellationToken);

        if (active.Profile.Commercial?.Connection is { Length: > 0 } named
            && !string.Equals(
                named.Trim(),
                active.Profile.BusinessCentralConnection?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.ProfileName}' names Business Central " +
                $"connection '{named.Trim()}' in its commercial setting, but its " +
                "businessCentralConnection is different. Set " +
                "businessCentralConnection instead; the commercial setting may " +
                "then leave the connection out.");
        }
    }

    private static CommercialSystem? Find(CommercialSelection selection) =>
        Systems.FirstOrDefault(system => selection.Is(system.Key));
}
