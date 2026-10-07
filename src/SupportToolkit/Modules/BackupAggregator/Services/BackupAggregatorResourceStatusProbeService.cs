using SupportToolkit.Providers.Acronis.ResourceManagement.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Privacy-safe diagnostic surface for comparing the Acronis
/// Resource Management workload population with backup-report coverage.
///
/// No resource names, IDs, tenant IDs, or customer payload values are emitted.
/// </summary>
public sealed class BackupAggregatorResourceStatusProbeService
{
    public int Probe(
        IReadOnlyList<ResourceStatusDto> resources)
    {
        Console.WriteLine();

        Console.WriteLine(
            "ACRONIS RESOURCE STATUS COVERAGE PROBE"
        );

        Console.WriteLine(
            "======================================"
        );

        Console.WriteLine();

        if (resources.Count == 0)
        {
            Console.WriteLine(
                "No resource statuses were returned."
            );

            return 1;
        }

        var structuralGroups =
            resources
                .Count(
                    resource =>
                        IsStructuralGroup(
                            resource.Context.Type
                        )
                );

        var nonGroupResources =
            resources.Count
            - structuralGroups;

        var resourcesWithAnyPolicy =
            resources
                .Count(
                    resource =>
                        resource.Policies is
                        {
                            Count: > 0
                        }
                );

        var resourcesWithBackupPolicy =
            resources
                .Count(
                    HasBackupPolicy
                );

        var nonGroupResourcesWithBackupPolicy =
            resources
                .Where(
                    resource =>
                        !IsStructuralGroup(
                            resource.Context.Type
                        )
                )
                .Count(
                    HasBackupPolicy
                );

        Console.WriteLine(
            "SUMMARY"
        );

        Console.WriteLine(
            "-------"
        );

        WriteSummaryRow(
            "Resource-status rows",
            resources.Count
        );

        WriteSummaryRow(
            "Structural group rows",
            structuralGroups
        );

        WriteSummaryRow(
            "Non-group resources",
            nonGroupResources
        );

        WriteSummaryRow(
            "Resources with any policy",
            resourcesWithAnyPolicy
        );

        WriteSummaryRow(
            "Resources with backup policy",
            resourcesWithBackupPolicy
        );

        WriteSummaryRow(
            "Non-group resources with backup policy",
            nonGroupResourcesWithBackupPolicy
        );

        Console.WriteLine();

        WriteResourceTypeSummary(
            resources
        );

        WritePolicyTypeSummary(
            resources
        );

        WriteAggregateStatusSummary(
            resources
        );

        return 0;
    }

    private static void WriteResourceTypeSummary(
        IReadOnlyList<ResourceStatusDto> resources)
    {
        Console.WriteLine(
            "RESOURCE TYPES"
        );

        Console.WriteLine(
            "--------------"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"{"Type",-42}" +
            $"{"Total",7}" +
            $"{"Backup",9}" +
            $"{"Last OK",10}" +
            $"{"Next",8}"
        );

        Console.WriteLine(
            new string(
                '-',
                76
            )
        );

        var groups =
            resources
                .GroupBy(
                    resource =>
                        Display(
                            resource.Context.Type
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
                );

        foreach (var group
                 in groups)
        {
            var items =
                group.ToList();

            var withBackupPolicy =
                items.Count(
                    HasBackupPolicy
                );

            var withLastSuccessfulBackup =
                items.Count(
                    HasLastSuccessfulBackup
                );

            var withNextBackup =
                items.Count(
                    HasNextBackup
                );

            Console.WriteLine(
                $"{Fit(group.Key, 40),-42}" +
                $"{items.Count,7}" +
                $"{withBackupPolicy,9}" +
                $"{withLastSuccessfulBackup,10}" +
                $"{withNextBackup,8}"
            );
        }

        Console.WriteLine();
    }

    private static void WritePolicyTypeSummary(
        IReadOnlyList<ResourceStatusDto> resources)
    {
        Console.WriteLine(
            "POLICY TYPES"
        );

        Console.WriteLine(
            "------------"
        );

        Console.WriteLine();

        var policyTypes =
            resources
                .SelectMany(
                    resource =>
                        resource.Policies
                        ?? []
                )
                .GroupBy(
                    policy =>
                        Display(
                            policy.Type
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

        if (policyTypes.Count == 0)
        {
            Console.WriteLine(
                "  <none>"
            );

            Console.WriteLine();

            return;
        }

        foreach (var group
                 in policyTypes)
        {
            Console.WriteLine(
                $"  {group.Key}: {group.Count()}"
            );
        }

        Console.WriteLine();
    }

    private static void WriteAggregateStatusSummary(
        IReadOnlyList<ResourceStatusDto> resources)
    {
        Console.WriteLine(
            "AGGREGATE STATUSES"
        );

        Console.WriteLine(
            "------------------"
        );

        Console.WriteLine();

        var statuses =
            resources
                .GroupBy(
                    resource =>
                        Display(
                            resource.Aggregate?.Status
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
                );

        foreach (var group
                 in statuses)
        {
            Console.WriteLine(
                $"  {group.Key}: {group.Count()}"
            );
        }

        Console.WriteLine();
    }

    private static bool HasBackupPolicy(
        ResourceStatusDto resource)
    {
        return resource.Policies?
            .Any(
                policy =>
                    !string.IsNullOrWhiteSpace(
                        policy.Type
                    )
                    && policy.Type.Contains(
                        "backup",
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            == true;
    }

    private static bool HasLastSuccessfulBackup(
        ResourceStatusDto resource)
    {
        return resource.Policies?
            .Any(
                policy =>
                    IsBackupPolicy(
                        policy.Type
                    )
                    && policy.LastSuccessRunTime
                    is not null
            )
            == true;
    }

    private static bool HasNextBackup(
        ResourceStatusDto resource)
    {
        return resource.Policies?
            .Any(
                policy =>
                    IsBackupPolicy(
                        policy.Type
                    )
                    && policy.NextRunTime
                    is not null
            )
            == true;
    }

    private static bool IsBackupPolicy(
        string? policyType)
    {
        return !string.IsNullOrWhiteSpace(
                   policyType
               )
               && policyType.Contains(
                   "backup",
                   StringComparison.OrdinalIgnoreCase
               );
    }

    private static bool IsStructuralGroup(
        string? resourceType)
    {
        return !string.IsNullOrWhiteSpace(
                   resourceType
               )
               && resourceType.StartsWith(
                   "resource.group.",
                   StringComparison.OrdinalIgnoreCase
               );
    }

    private static void WriteSummaryRow(
        string label,
        int value)
    {
        Console.WriteLine(
            $"{label,-42}{value}"
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

    private static string Fit(
        string value,
        int width)
    {
        if (value.Length <= width)
        {
            return value;
        }

        if (width <= 3)
        {
            return value[..width];
        }

        return
            value[..(width - 3)]
            + "...";
    }
}