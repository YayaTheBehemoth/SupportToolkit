using System.Globalization;

using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Reporting;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Fixture-only walk-through of customer mapping and reconciliation:
/// candidate key profiling, suggestion, simulated human decisions and
/// the shadow monitoring scope report.
///
/// Uses synthetic parties and an in-memory mapping store. No external
/// provider access, no persistence and no ticketing. A persistent
/// confirm/reject command waits on the mapping storage decision.
/// </summary>
public sealed class CustomerMappingFixtureCommand
{
    private const string FixtureActor =
        "fixture-reviewer";

    public async Task<int> RunAsync(
        TextWriter output,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        var parties =
            CustomerPartyFixtureFactory.Create();

        var repository =
            new InMemoryCustomerMappingRepository();

        output.WriteLine();
        output.WriteLine(
            "CUSTOMER MAPPING - FIXTURE INVESTIGATION"
        );
        output.WriteLine(
            "----------------------------------------"
        );
        output.WriteLine();

        output.WriteLine(
            "Source: Synthetic parties, in-memory mapping store"
        );

        output.WriteLine(
            "Evaluation date: " +
            asOfDate.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            )
        );

        output.WriteLine();
        output.WriteLine("CANDIDATE KEY PROFILE (COUNTS ONLY)");
        output.WriteLine("-----------------------------------");

        foreach (var profile in CandidateKeyProfiler.Profile(
            parties.Acronis,
            parties.BusinessCentral))
        {
            WriteProfile(output, profile);
        }

        var suggester =
            new CustomerMappingSuggester(repository);

        var suggestions =
            await suggester.SuggestAsync(
                parties.Acronis,
                parties.BusinessCentral,
                cancellationToken
            );

        output.WriteLine();
        output.WriteLine("SUGGESTIONS");
        output.WriteLine("-----------");

        foreach (var mapping in suggestions.Added)
        {
            output.WriteLine(
                $"  {mapping.AcronisTenantId} -> {mapping.BcCustomerNo} | " +
                $"{mapping.MatchMethod} | score " +
                (mapping.MatchScore ?? 0).ToString(
                    "0.0",
                    CultureInfo.InvariantCulture
                )
            );
        }

        output.WriteLine();
        output.WriteLine("EXCEPTIONS FOR MANUAL MAPPING");
        output.WriteLine("-----------------------------");

        foreach (var exception in suggestions.Exceptions)
        {
            output.WriteLine(
                $"  {exception.Kind}" +
                (exception.Key is null ? "" : $" on {exception.Key}") +
                $" | Acronis: {string.Join(", ", exception.AcronisTenantIds)}" +
                $" | BC: {string.Join(", ", exception.BcCustomerNumbers)}"
            );
        }

        /*
         * Loose-name preview. Runs against a throwaway copy of the
         * suggestions so the main flow and its counts are unchanged.
         */
        var previewRepository =
            new InMemoryCustomerMappingRepository();

        foreach (var added in suggestions.Added)
        {
            await previewRepository.AddAsync(
                added,
                cancellationToken
            );
        }

        var loose =
            await new CustomerMappingCandidateRanker(previewRepository)
                .RankAsync(
                    parties.Acronis,
                    parties.BusinessCentral,
                    cancellationToken
                );

        output.WriteLine();
        output.WriteLine("LOOSE NAME PREVIEW (NOT STORED)");
        output.WriteLine("-------------------------------");

        foreach (var candidate in loose.Candidates)
        {
            output.WriteLine(
                $"  {candidate.AcronisTenantId} -> " +
                $"{candidate.BcCustomerNo} | {candidate.Tier}"
            );
        }

        output.WriteLine(
            $"  Would suggest: {loose.Added.Count}, " +
            $"for manual review: {loose.Exceptions.Count}"
        );

        /*
         * Simulated reviewer decisions:
         * Acronis tenants carry name only, so every suggestion is a
         * name match. The reviewer rejects one, confirms the rest and
         * maps a customer whose names differ manually.
         */
        var confirmation =
            new CustomerMappingConfirmationService(repository);

        foreach (var mapping in suggestions.Added)
        {
            if (CustomerMappingRules.SameId(
                mapping.BcCustomerNo,
                "BC-ALPINE"))
            {
                await confirmation.RejectAsync(
                    mapping.Id,
                    FixtureActor,
                    "Name match not verified.",
                    cancellationToken
                );
            }
            else
            {
                await confirmation.ConfirmAsync(
                    mapping.Id,
                    FixtureActor,
                    cancellationToken: cancellationToken
                );
            }
        }

        await confirmation.AddManualAsync(
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "BC-CONTOSO",
            FixtureActor,
            "Names differ; mapped by reviewer.",
            cancellationToken
        );

        var rerun =
            await suggester.SuggestAsync(
                parties.Acronis,
                parties.BusinessCentral,
                cancellationToken
            );

        var mappings =
            await repository.GetAllAsync(cancellationToken);

        output.WriteLine();
        output.WriteLine("MAPPING STATES AFTER REVIEW");
        output.WriteLine("---------------------------");

        foreach (var state in Enum.GetValues<CustomerMappingState>())
        {
            output.WriteLine(
                $"  {state}: " +
                mappings.Count(mapping => mapping.State == state)
            );
        }

        output.WriteLine(
            $"  New suggestions on rerun: {rerun.Added.Count}"
        );

        /*
         * Shadow scope report over the synthetic subscriptions, with
         * an orphan line and an Open subscription added to exercise
         * the exception buckets.
         */
        var subscriptions =
            SmartSubscriptionFixtureFactory.Create();

        IReadOnlyList<SmartSubscriptionHeader> headers =
        [
            .. subscriptions.Headers,
            new(
                "SUB-FABRIKAM-2",
                "BC-FABRIKAM",
                "Open",
                new DateOnly(2026, 1, 1),
                null,
                "BC-FABRIKAM",
                " "
            )
        ];

        IReadOnlyList<SmartSubscriptionLine> lines =
        [
            .. subscriptions.Lines,
            new(
                "SUB-FABRIKAM-2",
                SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber,
                "Item",
                false
            ),
            new(
                "SUB-ORPHAN",
                SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber,
                "Item",
                false
            )
        ];

        IReadOnlyList<string> itemNumbers =
            [SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber];

        var tenants =
            MonitoringTenantSelectionFixtureFactory
                .Create()
                .Tenants;

        var observations =
            new SmartSubscriptionEntitlementEvaluator(itemNumbers)
                .Evaluate(
                    headers,
                    lines,
                    asOfDate
                );

        var selection =
            new MonitoringTenantSelector()
                .Select(
                    tenants,
                    observations,
                    await new ConfirmedMappingIdentitySource(repository)
                        .GetIdentitiesAsync(cancellationToken)
                );

        var reconciliation =
            new ReconciliationEngine()
                .Reconcile(
                    mappings,
                    headers,
                    lines,
                    AcronisTenantObservations.FromCustomerTenants(
                        tenants
                    ),
                    asOfDate,
                    SubscriptionItemNumbers.ToSet(itemNumbers)
                );

        new ConsoleMonitoringScopeReporter()
            .Write(
                output,
                MonitoringScopeReportBuilder.Build(
                    asOfDate,
                    itemNumbers,
                    observations,
                    selection,
                    reconciliation,
                    monitoredEvidenceSupplied: false,
                    tenants
                )
            );

        output.WriteLine();
        output.WriteLine("IMPORTANT");
        output.WriteLine("---------");

        output.WriteLine(
            "All parties, mappings and decisions are fictional."
        );

        output.WriteLine(
            "Mappings are held in memory and discarded on exit."
        );

        output.WriteLine(
            "Discrepancy types are provisional; no tickets are created."
        );

        output.WriteLine();

        return 0;
    }

    private static void WriteProfile(
        TextWriter output,
        CandidateKeyProfile profile)
    {
        output.WriteLine();
        output.WriteLine($"  Key: {profile.Key}");

        output.WriteLine(
            $"    1:1 {profile.OneToOneValues} | " +
            $"1:many {profile.OneToManyValues} | " +
            $"many:1 {profile.ManyToOneValues} | " +
            $"many:many {profile.ManyToManyValues}"
        );

        output.WriteLine(
            $"    No match: Acronis {profile.AcronisRecordsWithoutMatch} | " +
            $"BC {profile.BusinessCentralRecordsWithoutMatch}"
        );

        WriteSide(output, "Acronis", profile.Acronis);
        WriteSide(output, "BC", profile.BusinessCentral);
    }

    private static void WriteSide(
        TextWriter output,
        string label,
        KeySideProfile side)
    {
        output.WriteLine(
            $"    {label}: records {side.Records} | ok {side.Ok} | " +
            $"empty {side.Empty} | malformed {side.Malformed} | " +
            $"excluded {side.Excluded}"
        );
    }
}
