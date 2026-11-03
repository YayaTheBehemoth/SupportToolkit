using System.Text.Encodings.Web;
using System.Text.Json;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.BusinessCentral;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Developer-only, read-only dry run of customer matching.
///
/// Reads the entitled Business Central customers and the Acronis
/// customer tenants, runs the automatic resolver and reports how many
/// would map automatically, need review or stay unmatched. Nothing is
/// stored or written anywhere.
///
/// Counts only by default. Names and customer numbers print only with
/// an explicit --names request, for local review.
///
/// The CLI dispatcher must require an explicit --production argument.
/// </summary>
public sealed class CustomerMatchingDevCommand
{
    private static readonly JsonSerializerOptions NameJsonOptions =
        new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    public CustomerMatchingDevCommand(
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
        CancellationToken cancellationToken = default,
        bool explainUnmatched = false)
    {
        ArgumentNullException.ThrowIfNull(output);

        var itemNumbers =
            await _configurationResolver.GetMonitoringItemNumbersAsync(
                BackupAggregatorModule.ModuleCommand,
                cancellationToken);

        var bundle =
            await BusinessCentralProviderFactory.CreateAsync(
                _configurationResolver,
                cancellationToken);

        output.WriteLine("Reading Business Central (read-only)...");

        // The same shared services the scoped review uses, so this
        // dry run cannot drift from what the review will decide.
        var scope =
            await new MonitoringScopeService(
                new BusinessCentralSmartSubscriptionEvidenceSource(
                    bundle.Provider,
                    BusinessCentralSourceContext.From(bundle.Options)))
                .ResolveAsync(
                    itemNumbers,
                    DateOnly.FromDateTime(DateTime.Today),
                    cancellationToken,
                    await _configurationResolver
                        .GetServiceItemNumbersAsync(
                            BackupAggregatorModule.ModuleCommand,
                            cancellationToken));

        var observations = scope.Observations;

        var bcParties =
            await new BusinessCentralCustomerNameSource(bundle.Provider)
                .GetCustomerPartiesAsync(
                    scope.ServiceCustomerNumbers.ToArray(),
                    cancellationToken);

        output.WriteLine("Reading Acronis tenants (read-only)...");

        using var session =
            await AcronisDevSession.CreateAsync(
                _configurationResolver,
                cancellationToken);

        var tenants =
            await session.Provider.GetTenantsAsync(
                cancellationToken);

        var acronis =
            AcronisCustomerParties.ForResolution(tenants);

        var result =
            await new AutoCustomerMappingResolver()
                .ResolveAsync(
                    acronis.Parties,
                    bcParties.Parties,
                    cancellationToken: cancellationToken);

        WriteReport(
            output,
            observations,
            bcParties,
            acronis,
            result,
            showNames);

        if (explainUnmatched)
        {
            // Every customer with a subscription, matched or not, so a
            // similar customer outside the matched set can be found.
            var allCustomerNumbers =
                scope.Evidence.Headers
                    .Select(header => header.CustomerNumber)
                    .Where(number => !string.IsNullOrWhiteSpace(number))
                    .Select(number => number!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var allCustomers =
                await new BusinessCentralCustomerNameSource(bundle.Provider)
                    .GetCustomerPartiesAsync(
                        allCustomerNumbers,
                        cancellationToken);

            WriteDiagnoses(
                output,
                Explain(
                    acronis,
                    bcParties,
                    allCustomers,
                    scope.Evidence,
                    result),
                showNames);
        }

        return 0;
    }

    /// <summary>
    /// Why each tenant without an accepted link got none.
    /// </summary>
    public static IReadOnlyList<UnmatchedPartyDiagnosis> Explain(
        AcronisPartyBuild acronis,
        ScopedCustomerParties matched,
        ScopedCustomerParties allCustomers,
        SmartSubscriptionEvidence evidence,
        AutoMappingResult result)
    {
        var acceptedTenants =
            result.Accepted
                .Select(mapping => mapping.AcronisTenantId.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var linkedCustomers =
            result.Accepted
                .Select(mapping => mapping.BcCustomerNo.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A weak candidate is not an ambiguity; only genuine refusals
        // to choose between candidates count as flagged.
        var flagged =
            result.Exceptions
                .Where(
                    exception => exception.Kind
                        != CustomerMatchExceptionKind.WeakNameCandidate)
                .SelectMany(exception => exception.AcronisTenantIds)
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var customerBySubscription =
            evidence.Headers
                .Where(
                    header => !string.IsNullOrWhiteSpace(
                            header.SubscriptionNumber)
                        && !string.IsNullOrWhiteSpace(
                            header.CustomerNumber))
                .GroupBy(
                    header => header.SubscriptionNumber.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .ToDictionary(
                    group => group.Key,
                    group => group.Single().CustomerNumber!.Trim(),
                    StringComparer.OrdinalIgnoreCase);

        IReadOnlyDictionary<string, IReadOnlyList<string>> items =
            evidence.Lines
                .Where(
                    line => customerBySubscription.ContainsKey(
                        line.SubscriptionNumber.Trim()))
                .GroupBy(
                    line => customerBySubscription[
                        line.SubscriptionNumber.Trim()],
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group
                        .Select(line => line.ItemNumber.Trim())
                        .Where(item => item.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        return UnmatchedPartyDiagnostics.Diagnose(
            acronis.Parties
                .Where(party => !acceptedTenants.Contains(party.Id.Trim()))
                .ToArray(),
            allCustomers.Parties,
            matched.Parties
                .Select(party => party.Id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            linkedCustomers,
            flagged,
            items);
    }

    public static void WriteDiagnoses(
        TextWriter output,
        IReadOnlyList<UnmatchedPartyDiagnosis> diagnoses,
        bool showNames)
    {
        output.WriteLine();
        output.WriteLine("WHY TENANTS WENT UNMATCHED");
        output.WriteLine("--------------------------");
        output.WriteLine(
            $"Tenants without an accepted customer: {diagnoses.Count}");

        foreach (var group in diagnoses
            .GroupBy(diagnosis => diagnosis.Reason)
            .OrderBy(group => group.Key))
        {
            output.WriteLine(
                $"  {Describe(group.Key)}: {group.Count()}");
        }

        if (!showNames)
        {
            output.WriteLine();
            output.WriteLine(
                "Use --names to see each tenant and its closest customers.");
            return;
        }

        foreach (var diagnosis in diagnoses)
        {
            output.WriteLine();
            output.WriteLine(
                $"Acronis {Short(diagnosis.PartyId)} " +
                $"{NameOrNone(diagnosis.PartyName)} -> " +
                Describe(diagnosis.Reason));

            output.WriteLine(
                "    sees: " + Forensics(diagnosis.PartyName));

            if (diagnosis.Candidates.Count == 0)
            {
                output.WriteLine(
                    "    no customer with a similar name");
            }

            foreach (var candidate in diagnosis.Candidates)
            {
                output.WriteLine(
                    $"    BC {candidate.CustomerId} " +
                    $"{NameOrNone(candidate.CustomerName)} " +
                    $"[{candidate.Tier}] " +
                    (candidate.InMatchedSet
                        ? "matched set"
                        : "NOT in matched set") +
                    ", items: " +
                    (candidate.Items.Count == 0
                        ? "none"
                        : string.Join(", ", candidate.Items)));

                output.WriteLine(
                    "        sees: " + Forensics(candidate.CustomerName));
            }
        }
    }

    private static string Describe(
        UnmatchedReason reason)
    {
        return reason switch
        {
            UnmatchedReason.AmbiguousOrConflicting =>
                "ambiguous or conflicting candidates",
            UnmatchedReason.OnlyWeakNameOverlap =>
                "only a weak name overlap",
            UnmatchedReason.CandidateTakenByAnotherParty =>
                "best customer already linked to another tenant",
            UnmatchedReason.SimilarCustomerNotInScope =>
                "similar customer holds none of the service items",
            UnmatchedReason.NoSimilarCustomer =>
                "no similar customer in Business Central",
            _ => reason.ToString()
        };
    }

    /// <summary>
    /// The compact form the matcher compares, plus anything odd in the
    /// raw name.
    /// </summary>
    private static string Forensics(
        string? name)
    {
        var inspected = NameForensics.Inspect(name);

        return
            $"\"{inspected.Compact}\"" +
            (inspected.Oddities.Count == 0
                ? string.Empty
                : "; odd: " + string.Join(", ", inspected.Oddities));
    }

    private static string NameOrNone(
        string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? "(no name)"
            : JsonSerializer.Serialize(
                name,
                NameJsonOptions);
    }

    public static void WriteReport(
        TextWriter output,
        IReadOnlyList<MonitoringEntitlementObservation> observations,
        ScopedCustomerParties bc,
        AcronisPartyBuild acronis,
        AutoMappingResult result,
        bool showNames)
    {
        output.WriteLine();
        output.WriteLine("CUSTOMER MATCHING DRY RUN (nothing stored)");
        output.WriteLine("------------------------------------------");

        output.WriteLine();
        output.WriteLine("Business Central");
        output.WriteLine(
            $"  entitlement observations: {observations.Count} " +
            $"(active candidates " +
            $"{observations.Count(o => o.Assessment == MonitoringEntitlementAssessment.ActiveCandidate)}, " +
            $"needs review " +
            $"{observations.Count(o => o.Assessment == MonitoringEntitlementAssessment.NeedsReview)})");
        output.WriteLine(
            $"  customers matched:        {bc.Parties.Count} " +
            "(entitled, or holding a service item)");
        output.WriteLine(
            "  of which entitled:        " +
            CustomerEntitlementActivity.Classify(observations).Count);
        output.WriteLine(
            $"  with a name:              {bc.CustomersWithName}");
        output.WriteLine(
            $"  without a name:           {bc.CustomersWithoutName}");
        output.WriteLine(
            $"  conflicting names:        {bc.CustomersWithConflictingNames}");

        var nameFailure = bc.Failure;

        if (nameFailure is not null)
        {
            output.WriteLine(
                "  NAME READ FAILED: the customer names could not be " +
                "read, so no name match is possible.");
            output.WriteLine($"  {Clean(nameFailure)}");
        }

        output.WriteLine();
        output.WriteLine("Acronis");
        output.WriteLine(
            $"  customer tenants:         {acronis.Parties.Count}");
        output.WriteLine(
            "  operator tenant excluded: " +
            (acronis.OperatorTenantExcluded ? "yes" : "no") +
            $" (partner tenants seen: {acronis.PartnerTenants}, " +
            $"deleted customer tenants skipped: " +
            $"{acronis.DeletedCustomerTenants})");

        if (acronis.Notice is not null)
        {
            output.WriteLine($"  NOTICE: {acronis.Notice}");
        }

        output.WriteLine();
        output.WriteLine("Resolution");
        output.WriteLine(
            $"  accepted automatically:   {result.Accepted.Count}");

        foreach (var group in result.Accepted
            .GroupBy(mapping => mapping.MatchMethod)
            .OrderBy(group => group.Key))
        {
            output.WriteLine(
                $"    by {group.Key}: {group.Count()}");
        }

        output.WriteLine(
            $"  pending review:           {result.PendingReview.Count}");
        output.WriteLine(
            $"  exceptions:               {result.Exceptions.Count}");

        foreach (var group in result.Exceptions
            .GroupBy(exception => exception.Kind)
            .OrderBy(group => group.Key))
        {
            output.WriteLine(
                $"    {group.Key}: {group.Count()}");
        }

        // A customer whose subscriptions are all suspended or blocked
        // has no service to monitor, so having no tenant is no gap and
        // such a customer is left out of the lists below. It still took
        // part in matching, so a tenant that exists for one is found.
        var entitlement =
            CustomerEntitlementActivity.Classify(observations);

        CustomerEntitlementState StateOf(string number) =>
            entitlement.TryGetValue(number.Trim(), out var state)
                ? state
                : CustomerEntitlementState.NeedsReview;

        // Only entitled customers can be gaps; a customer that merely
        // holds a service item is expected to have no tenant.
        var gaps =
            result.UnmatchedBcCustomerNumbers
                .Where(
                    number => entitlement.ContainsKey(number.Trim())
                        && StateOf(number)
                            != CustomerEntitlementState.Inactive)
                .ToArray();

        output.WriteLine(
            "  BC customers with no candidate tenant: " +
            gaps.Length +
            $" (active {gaps.Count(number => StateOf(number) == CustomerEntitlementState.Active)}, " +
            $"unclear {gaps.Count(number => StateOf(number) == CustomerEntitlementState.NeedsReview)})");
        output.WriteLine(
            "  Acronis customer tenants with no candidate customer: " +
            result.UnmatchedAcronisIds.Count);

        if (!showNames)
        {
            output.WriteLine();
            output.WriteLine(
                "Names and numbers hidden. Use --names for a local " +
                "review worksheet.");
            return;
        }

        WriteWorksheet(output, bc, acronis, result, gaps, StateOf);
    }

    private static void WriteWorksheet(
        TextWriter output,
        ScopedCustomerParties bc,
        AcronisPartyBuild acronis,
        AutoMappingResult result,
        IReadOnlyList<string> bcGaps,
        Func<string, CustomerEntitlementState> stateOf)
    {
        var bcById =
            bc.Parties.ToDictionary(
                party => party.Id.Trim(),
                StringComparer.OrdinalIgnoreCase);

        var acronisById =
            acronis.Parties.ToDictionary(
                party => party.Id.Trim(),
                StringComparer.OrdinalIgnoreCase);

        string Pair(CustomerMappingRecord mapping) =>
            $"BC {mapping.BcCustomerNo.Trim()} " +
            $"{Name(bcById, mapping.BcCustomerNo)}  <->  " +
            $"Acronis {Short(mapping.AcronisTenantId)} " +
            $"{Name(acronisById, mapping.AcronisTenantId)}";

        output.WriteLine();
        output.WriteLine("WORKSHEET (local review, not stored)");
        output.WriteLine("------------------------------------");

        output.WriteLine("Accepted automatically:");
        foreach (var mapping in result.Accepted)
        {
            output.WriteLine(
                $"  [{mapping.MatchMethod} {mapping.MatchScore:0.00}] " +
                Pair(mapping));
        }

        output.WriteLine("Pending review:");
        foreach (var mapping in result.PendingReview)
        {
            output.WriteLine(
                $"  [{mapping.MatchMethod} {mapping.MatchScore:0.00}] " +
                Pair(mapping));
        }

        output.WriteLine("BC customers with no candidate tenant:");
        foreach (var number in bcGaps)
        {
            output.WriteLine(
                $"  [{stateOf(number)}] BC {number} {Name(bcById, number)}");
        }

        output.WriteLine("Acronis customer tenants with no candidate customer:");
        foreach (var id in result.UnmatchedAcronisIds)
        {
            output.WriteLine(
                $"  Acronis {Short(id)} {Name(acronisById, id)}");
        }
    }

    private static string Name(
        Dictionary<string, CustomerParty> parties,
        string id)
    {
        return parties.TryGetValue(id.Trim(), out var party)
            && !string.IsNullOrWhiteSpace(party.Name)
                ? JsonSerializer.Serialize(
                    party.Name,
                    NameJsonOptions)
                : "(no name)";
    }

    private static string Short(
        string id)
    {
        var trimmed = id.Trim();

        return trimmed[..Math.Min(8, trimmed.Length)];
    }

    // Server messages are shown trimmed and without control characters.
    private static string Clean(
        string message)
    {
        return new string(
            message
                .Where(character => !char.IsControl(character))
                .Take(200)
                .ToArray());
    }
}
