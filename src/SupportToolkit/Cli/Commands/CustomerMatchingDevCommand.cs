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
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Developer-only, read-only dry run of customer matching.
///
/// Reads the entitled Business Central customers and the Acronis
/// customer tenants, runs the automatic resolver and reports how many
/// would map automatically, need review or stay unmatched, and whether
/// the Acronis account number equals Business Central customer
/// numbers. Nothing is stored or written anywhere.
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
        CancellationToken cancellationToken = default)
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

        var evidence =
            await new BusinessCentralSmartSubscriptionEvidenceSource(
                bundle.Provider,
                BusinessCentralSourceContext.From(bundle.Options))
                .GetEvidenceAsync(cancellationToken);

        var observations =
            new SmartSubscriptionEntitlementEvaluator(itemNumbers)
                .Evaluate(
                    evidence.Headers,
                    evidence.Lines,
                    DateOnly.FromDateTime(DateTime.Today));

        var customerNumbers =
            observations
                .Select(observation => observation.CustomerNumber)
                .Where(number => !string.IsNullOrWhiteSpace(number))
                .Select(number => number!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        IReadOnlyList<BusinessCentralHeaderCustomerNameDto?> nameRows = [];
        string? nameFailure = null;

        try
        {
            nameRows =
                await bundle.Provider.GetHeaderCustomerNamesAsync(
                    cancellationToken);
        }
        catch (BusinessCentralApiException exception)
        {
            nameFailure = exception.Message;
        }

        var bcParties =
            BusinessCentralCustomerPartyMapper.Map(
                customerNumbers,
                nameRows);

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
            nameFailure,
            acronis,
            result,
            showNames);

        return 0;
    }

    public static void WriteReport(
        TextWriter output,
        IReadOnlyList<MonitoringEntitlementObservation> observations,
        BusinessCentralCustomerParties bc,
        string? nameFailure,
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
            $"  entitled customers:       {bc.Parties.Count}");
        output.WriteLine(
            $"  with a name:              {bc.CustomersWithName}");
        output.WriteLine(
            $"  without a name:           {bc.CustomersWithoutName}");
        output.WriteLine(
            $"  conflicting names:        {bc.CustomersWithConflictingNames}");

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

        var gaps =
            result.UnmatchedBcCustomerNumbers
                .Where(
                    number => StateOf(number)
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
        BusinessCentralCustomerParties bc,
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
