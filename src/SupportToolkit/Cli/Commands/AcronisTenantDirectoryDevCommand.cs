
using System.Text.Encodings.Web;
using System.Text.Json;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping.Matching;
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
        bool showFields = false,
        bool fullDetail = false,
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
                fullDetail ? "full" : "basic",
                cancellationToken);

        return WriteReport(
            output,
            tenants,
            showNames,
            showStructure,
            showFields);
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
        bool showStructure = false,
        bool showFields = false)
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

        if (showFields)
        {
            WriteFieldProfile(output, customers);
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

    /// <summary>
    /// Count-only profile of which fields customer tenants actually
    /// carry, to see whether a match key beyond the name exists.
    /// No field value is printed: only counts, field names and JSON
    /// value kinds. Reflects the data already fetched (lod=basic), so
    /// empty contacts here do not prove the API has none.
    /// </summary>
    private static void WriteFieldProfile(
        TextWriter output,
        IReadOnlyList<TenantDto> customers)
    {
        output.WriteLine();
        output.WriteLine("Field profile (customer tenants, counts only)");
        output.WriteLine("---------------------------------------------");
        output.WriteLine($"Customer tenants profiled: {customers.Count}");

        output.WriteLine(
            "customer_id present:  " +
            customers.Count(
                tenant => !string.IsNullOrWhiteSpace(tenant.CustomerId)));

        output.WriteLine(
            "contacts non-empty:   " +
            customers.Count(tenant => tenant.Contacts.Count > 0));

        output.WriteLine(
            "offering_items non-empty: " +
            customers.Count(tenant => tenant.OfferingItems.Count > 0));

        output.WriteLine();
        output.WriteLine("Unrecognized top-level fields (name: tenants, kinds)");

        var extra =
            customers
                .SelectMany(
                    tenant => tenant.AdditionalProperties
                        .Select(pair => (pair.Key, pair.Value.ValueKind)))
                .GroupBy(item => item.Key, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToList();

        if (extra.Count == 0)
        {
            output.WriteLine("  (none)");
        }

        foreach (var group in extra)
        {
            output.WriteLine(
                $"  {SafeName(group.Key)}: {group.Count()} " +
                string.Join(
                    "/",
                    group.Select(item => item.ValueKind)
                        .Distinct()
                        .OrderBy(kind => kind.ToString())));
        }

        var contacts =
            customers
                .SelectMany(
                    tenant => tenant.Contacts
                        .Where(
                            contact =>
                                contact.ValueKind == JsonValueKind.Object)
                        .Select(contact => (tenant, contact)))
                .ToList();

        output.WriteLine();
        output.WriteLine($"Contact objects: {contacts.Count}");

        foreach (var group in contacts
            .SelectMany(
                item => item.contact.EnumerateObject()
                    .Select(property => (property.Name, property.Value.ValueKind)))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            output.WriteLine(
                $"  contact.{SafeName(group.Key)}: {group.Count()} " +
                string.Join(
                    "/",
                    group.Select(item => item.ValueKind)
                        .Distinct()
                        .OrderBy(kind => kind.ToString())));
        }

        var emails =
            contacts
                .SelectMany(
                    item => item.contact.EnumerateObject()
                        .Where(
                            property =>
                                property.Value.ValueKind
                                    == JsonValueKind.String
                                && (property.Value.GetString() ?? "")
                                    .Contains('@'))
                        .Select(
                            property => (
                                item.tenant.Id,
                                Domain: DomainOf(
                                    property.Value.GetString()!))))
                .Where(item => item.Domain.Length > 0)
                .ToList();

        var perTenantDomains =
            emails
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(
                    group => group
                        .Select(item => item.Domain)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray())
                .ToList();

        var domainTenantCounts =
            emails
                .GroupBy(
                    item => item.Domain,
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    group => (
                        Domain: group.Key,
                        Tenants: group
                            .Select(item => item.Id)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count()))
                .ToList();

        output.WriteLine();
        output.WriteLine(
            $"Tenants with an e-mail-like value: {perTenantDomains.Count}");
        output.WriteLine(
            $"Distinct e-mail domains:           {domainTenantCounts.Count}");
        output.WriteLine(
            "Domains shared by >1 tenant:       " +
            domainTenantCounts.Count(item => item.Tenants > 1));
        output.WriteLine(
            "Free-mail domains in use:          " +
            domainTenantCounts.Count(
                item => PartyKeyNormalizer.PublicMailDomains
                    .Contains(item.Domain.ToLowerInvariant())));

        WriteContactObjectProfile(output, customers);

        output.WriteLine();
        output.WriteLine(
            "Name-only keys: duplicate names after trimming and case " +
            "folding: " +
            customers
                .Where(tenant => !string.IsNullOrWhiteSpace(tenant.Name))
                .GroupBy(
                    tenant => tenant.Name.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Sum(group => Math.Max(0, group.Count() - 1)));
    }

    /// <summary>
    /// The tenants carry a singular "contact" object that the DTO does
    /// not model. Reports which keys are filled, never their values.
    /// </summary>
    private static void WriteContactObjectProfile(
        TextWriter output,
        IReadOnlyList<TenantDto> customers)
    {
        var objects =
            customers
                .Select(
                    tenant => (
                        tenant.Id,
                        Present: tenant.AdditionalProperties
                            .TryGetValue("contact", out var element),
                        Element: element))
                .Where(
                    item => item.Present
                        && item.Element.ValueKind == JsonValueKind.Object)
                .ToList();

        output.WriteLine();
        output.WriteLine($"'contact' objects: {objects.Count}");

        var properties =
            objects
                .SelectMany(
                    item => item.Element.EnumerateObject()
                        .Select(
                            property => (
                                item.Id,
                                property.Name,
                                property.Value)))
                .ToList();

        foreach (var group in properties
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var filled =
                group.Count(
                    item => item.Value.ValueKind switch
                    {
                        JsonValueKind.String =>
                            !string.IsNullOrWhiteSpace(
                                item.Value.GetString()),
                        JsonValueKind.Null or JsonValueKind.Undefined =>
                            false,
                        _ => true
                    });

            var distinct =
                group
                    .Where(
                        item => item.Value.ValueKind
                            == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(
                                item.Value.GetString()))
                    .Select(
                        item => item.Value.GetString()!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

            output.WriteLine(
                $"  contact.{SafeName(group.Key)}: present " +
                $"{group.Count()}, filled {filled}, distinct {distinct}, " +
                string.Join(
                    "/",
                    group.Select(item => item.Value.ValueKind)
                        .Distinct()
                        .OrderBy(kind => kind.ToString())));
        }

        var emailDomains =
            properties
                .Where(
                    item => item.Value.ValueKind == JsonValueKind.String
                        && (item.Value.GetString() ?? "").Contains('@'))
                .Select(
                    item => (
                        item.Id,
                        Domain: DomainOf(item.Value.GetString()!)))
                .Where(item => item.Domain.Length > 0)
                .ToList();

        var domains =
            emailDomains
                .GroupBy(
                    item => item.Domain,
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    group => (
                        Domain: group.Key,
                        Tenants: group
                            .Select(item => item.Id)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count()))
                .ToList();

        output.WriteLine(
            "  tenants with an e-mail-like value: " +
            emailDomains
                .Select(item => item.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        output.WriteLine(
            $"  distinct e-mail domains: {domains.Count}");
        output.WriteLine(
            "  domains shared by >1 tenant: " +
            domains.Count(item => item.Tenants > 1));
        output.WriteLine(
            "  free-mail domains in use: " +
            domains.Count(
                item => PartyKeyNormalizer.PublicMailDomains
                    .Contains(item.Domain.ToLowerInvariant())));

        foreach (var key in new[] { "aan", "phone" })
        {
            WriteValueShape(
                output,
                key,
                properties
                    .Where(item => item.Name == key)
                    .Select(item => item.Value));
        }
    }

    /// <summary>
    /// Shape of a filled string field: lengths and character classes
    /// only, so it can be compared with another system's identifiers
    /// without printing any value.
    /// </summary>
    private static void WriteValueShape(
        TextWriter output,
        string key,
        IEnumerable<JsonElement> values)
    {
        var filled =
            values
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => (value.GetString() ?? string.Empty).Trim())
                .Where(text => text.Length > 0)
                .ToList();

        output.WriteLine();
        output.WriteLine($"  shape of contact.{key} ({filled.Count} filled):");

        if (filled.Count == 0)
        {
            return;
        }

        output.WriteLine(
            "    digits only: " +
            filled.Count(text => text.All(char.IsDigit)));
        output.WriteLine(
            "    exactly 8 digits: " +
            filled.Count(
                text => text.Length == 8 && text.All(char.IsDigit)));
        output.WriteLine(
            "    contains letters: " +
            filled.Count(text => text.Any(char.IsLetter)));
        output.WriteLine(
            "    contains spaces or symbols: " +
            filled.Count(
                text => text.Any(
                    character => !char.IsLetterOrDigit(character))));

        output.WriteLine(
            "    lengths: " +
            string.Join(
                ", ",
                filled
                    .GroupBy(text => text.Length)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}x{group.Count()}")));
    }

    private static string DomainOf(
        string value)
    {
        var at = value.LastIndexOf('@');

        return at < 0
            ? string.Empty
            : value[(at + 1)..].Trim().TrimEnd('>', ' ', '.');
    }

    // Field names come from the service, so keep them printable and short.
    private static string SafeName(
        string name)
    {
        var cleaned =
            new string(
                name.Where(c => !char.IsControl(c)).Take(40).ToArray());

        return cleaned;
    }

    private static string Short(
        string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? "-       "
            : id.Trim()[..Math.Min(8, id.Trim().Length)];
    }
}
