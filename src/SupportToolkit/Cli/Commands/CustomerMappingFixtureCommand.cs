using System.Globalization;

using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Fixture-only walk-through of customer mapping and reconciliation:
/// candidate key profiling, suggestion, simulated human decisions and
/// shadow reconciliation.
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
         * Shadow reconciliation over the synthetic subscriptions,
         * with an orphan line and an Open subscription added to
         * exercise the exception buckets.
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
                "SUB-ORPHAN",
                SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber,
                "Item",
                false
            )
        ];

        var report =
            new ReconciliationEngine()
                .Reconcile(
                    mappings,
                    headers,
                    lines,
                    AcronisTenantObservations.FromCustomerTenants(
                        MonitoringTenantSelectionFixtureFactory
                            .Create()
                            .Tenants
                    ),
                    asOfDate
                );

        output.WriteLine();
        output.WriteLine("SHADOW RECONCILIATION");
        output.WriteLine("---------------------");

        output.WriteLine($"Period: {report.Period}");

        foreach (var bucket in Enum.GetValues<ReconciliationBucket>())
        {
            output.WriteLine(
                $"  {bucket}: {report.Count(bucket)}"
            );
        }

        output.WriteLine();
        output.WriteLine("WOULD-BE TICKETS (NOT CREATED)");
        output.WriteLine("------------------------------");

        foreach (var type in Enum.GetValues<DiscrepancyType>())
        {
            output.WriteLine(
                $"  {type}: {report.Count(type)}"
            );
        }

        output.WriteLine();
        output.WriteLine("EXCEPTION QUEUE");
        output.WriteLine("---------------");

        foreach (var result in report.Results
            .Where(
                result =>
                    result.Bucket
                        is ReconciliationBucket.Unscoped
                        or ReconciliationBucket.Ambiguous))
        {
            output.WriteLine(
                $"  {result.Bucket} | {result.Subject} | " +
                $"{result.SubjectId} | {string.Join(" ", result.Reasons)}"
            );
        }

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
