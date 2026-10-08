using SupportToolkit.Providers.Acronis.O365.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Privacy-safe diagnostic for the Microsoft 365 workload inventory.
///
/// Resource names, IDs, email addresses, tenant IDs, account IDs, and
/// locators are intentionally never written to the console.
/// </summary>
public sealed class BackupAggregatorO365ResourceProbeService
{
    public int Probe(
        IReadOnlyList<AcronisO365ResourceDto> rawResources,
        int groupsQueried)
    {
        var uniqueResources =
            Deduplicate(
                rawResources,
                out var rowsWithoutStableIdentity
            );

        var duplicateRows =
            rawResources.Count
            - uniqueResources.Count;

        Console.WriteLine();

        Console.WriteLine(
            "ACRONIS O365 RESOURCE COVERAGE PROBE"
        );

        Console.WriteLine(
            "==================================="
        );

        Console.WriteLine();

        Console.WriteLine(
            "SUMMARY"
        );

        Console.WriteLine(
            "-------"
        );

        WriteSummaryRow(
            "Leaf groups queried",
            groupsQueried
        );

        WriteSummaryRow(
            "Raw resource rows",
            rawResources.Count
        );

        WriteSummaryRow(
            "Unique resources",
            uniqueResources.Count
        );

        WriteSummaryRow(
            "Duplicate rows collapsed",
            duplicateRows
        );

        WriteSummaryRow(
            "Rows without stable identity",
            rowsWithoutStableIdentity
        );

        WriteSummaryRow(
            "Resources with protection",
            uniqueResources.Count(
                HasProtection
            )
        );

        WriteSummaryRow(
            "Resources without protection",
            uniqueResources.Count(
                HasKnownNoProtection
            )
        );

        WriteSummaryRow(
            "Unknown protection state",
            uniqueResources.Count(
                resource =>
                    !HasProtection(
                        resource
                    )
                    && !HasKnownNoProtection(
                        resource
                    )
            )
        );

        Console.WriteLine();

        WriteVocabulary(
            "RESOURCE KINDS",
            uniqueResources.Select(
                resource =>
                    resource.Kind
            )
        );

        WriteVocabulary(
            "BASIC KINDS",
            uniqueResources
                .SelectMany(
                    resource =>
                        resource.BasicKinds
                )
                .Select(
                    basicKind =>
                        basicKind.Kind
                )
        );

        WriteVocabulary(
            "RESOURCE TYPES",
            uniqueResources.Select(
                resource =>
                    resource.ResourceType
            )
        );

        WriteVocabulary(
            "LAST TASK STATUSES",
            uniqueResources.Select(
                resource =>
                    resource.LastTaskStatus
            )
        );

        WriteVocabulary(
            "LAST TASK STATES",
            uniqueResources.Select(
                resource =>
                    resource.LastTaskState
            )
        );

        WriteBackupTimingSummary(
            uniqueResources
        );

        return 0;
    }

    private static IReadOnlyList<AcronisO365ResourceDto>
        Deduplicate(
            IReadOnlyList<AcronisO365ResourceDto> resources,
            out int rowsWithoutStableIdentity)
    {
        var unique =
            new Dictionary<
                string,
                AcronisO365ResourceDto>(
                    StringComparer.OrdinalIgnoreCase
                );

        var withoutIdentity =
            new List<AcronisO365ResourceDto>();

        foreach (var resource
                 in resources)
        {
            var identity =
                GetStableIdentity(
                    resource
                );

            if (identity is null)
            {
                withoutIdentity.Add(
                    resource
                );

                continue;
            }

            unique[identity] =
                resource;
        }

        rowsWithoutStableIdentity =
            withoutIdentity.Count;

        return unique
            .Values
            .Concat(
                withoutIdentity
            )
            .ToList()
            .AsReadOnly();
    }

    private static string? GetStableIdentity(
        AcronisO365ResourceDto resource)
    {
        if (!string.IsNullOrWhiteSpace(
                resource.Id))
        {
            return
                $"id:{resource.Id}";
        }

        if (!string.IsNullOrWhiteSpace(
                resource.InternalId))
        {
            return
                $"internal:{resource.InternalId}";
        }

        return null;
    }

    private static bool HasProtection(
        AcronisO365ResourceDto resource)
    {
        if (resource.HasProtections
            == true)
        {
            return true;
        }

        return resource.BasicKinds
            .Any(
                basicKind =>
                    basicKind.HasProtections
                    == true
            );
    }

    private static bool HasKnownNoProtection(
        AcronisO365ResourceDto resource)
    {
        if (resource.HasProtections
            == true)
        {
            return false;
        }

        if (resource.BasicKinds.Any(
                basicKind =>
                    basicKind.HasProtections
                    == true
            ))
        {
            return false;
        }

        return resource.HasProtections
            == false;
    }

    private static void WriteBackupTimingSummary(
        IReadOnlyList<AcronisO365ResourceDto> resources)
    {
        Console.WriteLine(
            "BACKUP TIMING DATA"
        );

        Console.WriteLine(
            "------------------"
        );

        Console.WriteLine();

        WriteSummaryRow(
            "With last start time",
            resources.Count(
                resource =>
                    resource.LastStartTime
                    is not null
            )
        );

        WriteSummaryRow(
            "With last finish time",
            resources.Count(
                resource =>
                    resource.LastFinishTime
                    is not null
            )
        );

        WriteSummaryRow(
            "With last successful backup",
            resources.Count(
                resource =>
                    resource.LastSuccessTime
                    is not null
            )
        );

        WriteSummaryRow(
            "With next backup time",
            resources.Count(
                resource =>
                    resource.NextStartTime
                    is not null
            )
        );

        Console.WriteLine();
    }

    private static void WriteVocabulary(
        string title,
        IEnumerable<string?> values)
    {
        Console.WriteLine(
            title
        );

        Console.WriteLine(
            new string(
                '-',
                title.Length
            )
        );

        Console.WriteLine();

        var groups =
            values
                .Select(
                    Display
                )
                .GroupBy(
                    value =>
                        value,
                    StringComparer.OrdinalIgnoreCase
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                )
                .ThenBy(
                    group =>
                        group.Key,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        if (groups.Count == 0)
        {
            Console.WriteLine(
                "  <none>"
            );
        }
        else
        {
            foreach (var group
                     in groups)
            {
                Console.WriteLine(
                    $"  {group.Key}: {group.Count()}"
                );
            }
        }

        Console.WriteLine();
    }

    private static void WriteSummaryRow(
        string label,
        int value)
    {
        Console.WriteLine(
            $"{label,-34}{value}"
        );
    }

    private static string Display(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "<missing>"
            : value;
    }
}