using SupportToolkit.Providers.Acronis.O365;
using SupportToolkit.Providers.Acronis.O365.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Privacy-safe diagnostic for Microsoft 365 group discovery.
///
/// Customer names, group names, resource names, IDs, account IDs,
/// tenant IDs, and locators are intentionally not emitted.
/// </summary>
public sealed class BackupAggregatorO365DiscoveryProbeService
{
    public int Probe(
        AcronisO365DiscoveryResult discovery)
    {
        Console.WriteLine();

        Console.WriteLine(
            "ACRONIS O365 DISCOVERY PROBE"
        );

        Console.WriteLine(
            "============================"
        );

        Console.WriteLine();

        Console.WriteLine(
            "SUMMARY"
        );

        Console.WriteLine(
            "-------"
        );

        WriteSummaryRow(
            "Groups discovered",
            discovery.Groups.Count
        );

        WriteSummaryRow(
            "Leaf groups",
            discovery.Groups.Count(
                group =>
                    group.Leaf == true
            )
        );

        WriteSummaryRow(
            "Non-leaf groups",
            discovery.Groups.Count(
                group =>
                    group.Leaf == false
            )
        );

        WriteSummaryRow(
            "Unknown leaf state",
            discovery.Groups.Count(
                group =>
                    group.Leaf is null
            )
        );

        WriteSummaryRow(
            "Custom groups",
            discovery.Groups.Count(
                group =>
                    group.Custom == true
            )
        );

        WriteSummaryRow(
            "Groups advertising O365 resources",
            discovery.Groups.Count(
                HasO365ResourceType
            )
        );

        Console.WriteLine();

        WriteGroupKindSummary(
            discovery
        );

        WriteGroupTypeSummary(
            discovery
        );

        WriteResourceTypeSummary(
            discovery
        );

        return 0;
    }

    private static void WriteGroupKindSummary(
        AcronisO365DiscoveryResult discovery)
    {
        Console.WriteLine(
            "GROUP KINDS"
        );

        Console.WriteLine(
            "-----------"
        );

        Console.WriteLine();

        var groups =
            discovery.Groups
                .GroupBy(
                    group =>
                        Display(
                            group.GroupKind
                            ?? group.Kind
                            ?? group.Type
                        ),
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

            Console.WriteLine();

            return;
        }

        foreach (var group
                 in groups)
        {
            Console.WriteLine(
                $"  {group.Key}: {group.Count()}"
            );
        }

        Console.WriteLine();
    }

    private static void WriteGroupTypeSummary(
        AcronisO365DiscoveryResult discovery)
    {
        Console.WriteLine(
            "GROUP TYPES"
        );

        Console.WriteLine(
            "-----------"
        );

        Console.WriteLine();

        var groups =
            discovery.Groups
                .GroupBy(
                    group =>
                        group.GroupType?.ToString()
                        ?? "<missing>",
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

            Console.WriteLine();

            return;
        }

        foreach (var group
                 in groups)
        {
            Console.WriteLine(
                $"  {group.Key}: {group.Count()}"
            );
        }

        Console.WriteLine();
    }

    private static void WriteResourceTypeSummary(
        AcronisO365DiscoveryResult discovery)
    {
        Console.WriteLine(
            "ADVERTISED RESOURCE TYPES"
        );

        Console.WriteLine(
            "-------------------------"
        );

        Console.WriteLine();

        var resourceTypes =
            discovery.Groups
                .SelectMany(
                    group =>
                        group.ResourceTypes
                )
                .Where(
                    resourceType =>
                        !string.IsNullOrWhiteSpace(
                            resourceType
                        )
                )
                .GroupBy(
                    resourceType =>
                        resourceType,
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

        if (resourceTypes.Count == 0)
        {
            Console.WriteLine(
                "  <none>"
            );

            Console.WriteLine();

            return;
        }

        foreach (var group
                 in resourceTypes)
        {
            Console.WriteLine(
                $"  {group.Key}: {group.Count()}"
            );
        }

        Console.WriteLine();
    }

    private static bool HasO365ResourceType(
        AcronisO365GroupDto group)
    {
        return group.ResourceTypes
            .Any(
                resourceType =>
                    resourceType.Contains(
                        "o365",
                        StringComparison.OrdinalIgnoreCase
                    )
            );
    }

    private static void WriteSummaryRow(
        string label,
        int value)
    {
        Console.WriteLine(
            $"{label,-40}{value}"
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