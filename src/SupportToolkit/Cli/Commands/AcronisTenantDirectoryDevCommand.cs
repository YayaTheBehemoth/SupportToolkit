
using System.Text.Encodings.Web;
using System.Text.Json;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Developer-only, read-only Acronis tenant directory inspection.
///
/// Uses the active profile's configured Acronis connection, independently
/// of BackupHealth and BackupAggregator module modes.
///
/// The CLI dispatcher must require an explicit --production argument.
/// </summary>
public sealed class AcronisTenantDirectoryDevCommand
{
    private static readonly JsonSerializerOptions NameJsonOptions =
        new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    public AcronisTenantDirectoryDevCommand(
        SupportToolkitConfigurationResolver configurationResolver)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver));
    }

    public async Task<int> RunAsync(
        TextWriter output,
        bool showNames,
        bool showStructure = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        /*
         * Resolve the existing Acronis connection.
         * Never accept credentials on the command line.
         */
        var connection =
            await _configurationResolver.GetAcronisConnectionAsync(
                cancellationToken);

        var options =
            AcronisOptions.Create(
                connection.DatacenterUrl,
                connection.ClientId,
                connection.ClientSecret);

        /*
         * Preserve the established read-only Acronis boundary.
         * No external write endpoints are available to this command.
         */
        using var httpClient =
            new HttpClient(
                new AcronisReadOnlyHandler(
                    options.DatacenterUrl,
                    new HttpClientHandler
                    {
                        AllowAutoRedirect = false
                    }))
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

        var logger =
            OperationalLogger.FromEnvironment();

        var diagnosticLogger =
            logger.DebugEnabled ? logger : null;

        var apiClient =
            new AcronisApiClient(
                httpClient,
                options,
                logger: diagnosticLogger);

        var provider =
            new AcronisTenantProvider(
                apiClient,
                diagnosticLogger);

        /*
         * This is the same production tenant-discovery implementation
         * used by BackupHealth and BackupAggregator.
         *
         * Its shared paginator fetches every page or fails.
         * No backup resource inventory is requested.
         */
        var tenants =
            await provider.GetTenantsAsync(
                cancellationToken);

        return WriteReport(
            output,
            tenants,
            showNames,
            showStructure);
    }

    /// <summary>
    /// Reports the complete tenant-directory result.
    ///
    /// No partial results are printed if provider pagination fails.
    /// Customer names are printed only on explicit request.
    /// </summary>
    public static int WriteReport(
        TextWriter output,
        IReadOnlyList<TenantDto> tenants,
        bool showNames,
        bool showStructure = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(tenants);

        var nullRows =
            tenants.Count(tenant => tenant is null);

        var validRows =
            tenants
                .Where(tenant => tenant is not null)
                .ToList();

        var customers =
            validRows
                .Where(tenant =>
                    string.Equals(
                        tenant.Kind,
                        "customer",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        var missingKinds =
            validRows.Count(tenant =>
                string.IsNullOrWhiteSpace(tenant.Kind));

        var otherKinds =
            validRows.Count(tenant =>
                !string.IsNullOrWhiteSpace(tenant.Kind)
                && !string.Equals(
                    tenant.Kind,
                    "customer",
                    StringComparison.OrdinalIgnoreCase));

        var blankNames =
            validRows.Count(tenant =>
                string.IsNullOrWhiteSpace(tenant.Name));

        var invalidIds = 0;
        var duplicateIds = 0;

        var seenIds = new HashSet<Guid>();

        foreach (var tenant in validRows)
        {
            if (!Guid.TryParse(tenant.Id, out var id))
            {
                invalidIds++;
                continue;
            }

            if (!seenIds.Add(id))
            {
                duplicateIds++;
            }
        }

        var duplicateCustomerNames =
            customers
                .Where(tenant =>
                    !string.IsNullOrWhiteSpace(tenant.Name))
                .GroupBy(
                    tenant => tenant.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Sum(group =>
                    Math.Max(0, group.Count() - 1));

        output.WriteLine("Acronis Tenant Directory");
        output.WriteLine("======================");
        output.WriteLine();

        output.WriteLine(
            $"Total tenant records:  {tenants.Count}");

        output.WriteLine(
            $"Customer tenants:      {customers.Count}");

        output.WriteLine(
            $"Other tenant kinds:    {otherKinds}");

        output.WriteLine(
            $"Missing tenant kinds:  {missingKinds}");

        output.WriteLine();
        output.WriteLine("Contract diagnostics");
        output.WriteLine("--------------------");

        output.WriteLine(
            $"Invalid UUIDs:         {invalidIds}");

        output.WriteLine(
            $"Duplicate UUIDs:       {duplicateIds}");

        output.WriteLine(
            $"Blank tenant names:    {blankNames}");

        output.WriteLine(
            $"Null tenant records:   {nullRows}");

        output.WriteLine(
            $"Duplicate customer names: {duplicateCustomerNames}");

        output.WriteLine();

        /*
         * Duplicate display names are observations rather than
         * necessarily invalid tenants. Different customer UUIDs
         * can legitimately share a name.
         */
        var incomplete =
            tenants.Count == 0
            || nullRows > 0
            || invalidIds > 0
            || duplicateIds > 0
            || blankNames > 0
            || missingKinds > 0;

        if (incomplete)
        {
            output.WriteLine(
                "WARNING: The tenant directory has completeness " +
                "or identity anomalies.");
            output.WriteLine();
        }

        if (showNames)
        {
            output.WriteLine(
                "Customer names (local review)");
            output.WriteLine(
                "-----------------------------");

            /*
             * JSON encoding protects terminal line boundaries
             * from embedded newlines or control characters.
             *
             * Names are not written to files by this command.
             * Duplicate names are intentionally preserved.
             */
            foreach (var tenant in customers
                         .OrderBy(
                             tenant => tenant.Name,
                             StringComparer.OrdinalIgnoreCase)
                         .ThenBy(
                             tenant => tenant.Id,
                             StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(tenant.Name))
                {
                    output.WriteLine(
                        "<missing customer name>");
                    continue;
                }

                output.WriteLine(
                    JsonSerializer.Serialize(
                        tenant.Name,
                        NameJsonOptions));
            }

            output.WriteLine();
            output.WriteLine(
                $"Customer rows listed: {customers.Count}");
        }
        else
        {
            output.WriteLine(
                "Customer names hidden. Use --names " +
                "for explicit local membership review.");
        }

        if (showStructure)
        {
            WriteStructure(output, validRows, showNames);
        }

        output.WriteLine();
        output.WriteLine(
            "Scope: configured Acronis API client's tenant subtree.");

        output.WriteLine(
            "Compare membership against the portal; " +
            "counts alone do not prove completeness.");

        return incomplete ? 2 : 0;
    }

    /// <summary>
    /// Hierarchy evidence for telling tenants apart without a name.
    /// Identifiers are shortened to a prefix; nothing is written to
    /// files. Names appear only when requested.
    /// </summary>
    private static void WriteStructure(
        TextWriter output,
        IReadOnlyList<TenantDto> tenants,
        bool showNames)
    {
        var childCounts =
            tenants
                .Where(tenant => !string.IsNullOrWhiteSpace(tenant.ParentId))
                .GroupBy(
                    tenant => tenant.ParentId!,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.OrdinalIgnoreCase);

        var ids =
            tenants
                .Select(tenant => tenant.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var outsideParents =
            childCounts.Keys.Where(parent => !ids.Contains(parent)).ToList();

        output.WriteLine();
        output.WriteLine("Structure");
        output.WriteLine("---------");

        output.WriteLine(
            "Parents outside the listed tenants: " +
            outsideParents.Count);

        foreach (var parent in outsideParents)
        {
            output.WriteLine(
                $"  parent {Short(parent)} has " +
                $"{childCounts[parent]} listed children");
        }

        output.WriteLine();

        foreach (var tenant in tenants
            .OrderBy(tenant => tenant.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tenant => tenant.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tenant => tenant.Id, StringComparer.OrdinalIgnoreCase))
        {
            var children =
                childCounts.GetValueOrDefault(tenant.Id);

            var name =
                showNames
                    ? JsonSerializer.Serialize(
                        tenant.Name,
                        NameJsonOptions)
                    : "(hidden)";

            output.WriteLine(
                $"{tenant.Kind,-9} " +
                $"id={Short(tenant.Id)} " +
                $"parent={Short(tenant.ParentId)} " +
                $"children={children,3} " +
                $"enabled={(tenant.Enabled?.ToString() ?? "?"),-5} " +
                $"{name}");
        }
    }

    private static string Short(
        string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? "-       "
            : id.Trim()[..Math.Min(8, id.Trim().Length)];
    }
}
