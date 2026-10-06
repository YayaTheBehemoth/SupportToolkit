using System.Text.Json;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Temporary diagnostic service used while mapping Acronis API data
/// to the existing Backup Status report.
/// </summary>
public sealed class BackupAggregatorProbeService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.SnakeCaseLower
        };

    public int Probe(
        IReadOnlyList<ResourceStatusDto> resources,
        string resourceName)
    {
        var serializedResources =
            Serialize(
                resources
            );

        var matches =
            serializedResources
                .Where(
                    resource =>
                        MatchesName(
                            resource,
                            resourceName
                        )
                )
                .ToList();

        if (matches.Count == 0)
        {
            Console.WriteLine();

            Console.WriteLine(
                $"No resource named '{resourceName}' was found."
            );

            Console.WriteLine(
                "Use the 'list' command to inspect the names " +
                "actually returned by this customer scope."
            );

            return 1;
        }

        Console.WriteLine();

        Console.WriteLine(
            "Backup Aggregator API Probe"
        );

        Console.WriteLine(
            "==========================="
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Matching resources: {matches.Count}"
        );

        foreach (var match
                 in matches)
        {
            WriteResource(
                match
            );
        }

        return 0;
    }

    public int List(
        IReadOnlyList<ResourceStatusDto> resources)
    {
        var serializedResources =
            Serialize(
                resources
            );

        Console.WriteLine();

        Console.WriteLine(
            "Customer-scoped resources"
        );

        Console.WriteLine(
            "========================="
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Resources returned: {serializedResources.Count}"
        );

        Console.WriteLine();

        foreach (var resource
                 in serializedResources
                     .OrderBy(
                         resource =>
                             GetString(
                                 resource,
                                 "context",
                                 "name"
                             ),
                         StringComparer.OrdinalIgnoreCase
                     ))
        {
            var name =
                GetString(
                    resource,
                    "context",
                    "name"
                )
                ?? "<missing>";

            var userDefinedName =
                GetString(
                    resource,
                    "context",
                    "user_defined_name"
                )
                ?? "<missing>";

            var type =
                GetString(
                    resource,
                    "context",
                    "type"
                )
                ?? "<missing>";

            Console.WriteLine(
                $"Name: {name}"
            );

            if (!string.Equals(
                    name,
                    userDefinedName,
                    StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"  Display name: {userDefinedName}"
                );
            }

            Console.WriteLine(
                $"  Type: {type}"
            );
        }

        Console.WriteLine();

        return 0;
    }

    private static List<JsonElement> Serialize(
        IReadOnlyList<ResourceStatusDto> resources)
    {
        return resources
            .Select(
                resource =>
                    JsonSerializer.SerializeToElement(
                        resource,
                        JsonOptions
                    )
            )
            .ToList();
    }

    private static bool MatchesName(
        JsonElement resource,
        string resourceName)
    {
        var name =
            GetString(
                resource,
                "context",
                "name"
            );

        var userDefinedName =
            GetString(
                resource,
                "context",
                "user_defined_name"
            );

        return string.Equals(
                name,
                resourceName,
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                userDefinedName,
                resourceName,
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static void WriteResource(
        JsonElement resource)
    {
        Console.WriteLine();

        Console.WriteLine(
            $"Resource: " +
            $"{GetString(resource, "context", "name") ?? "<missing>"}"
        );

        Console.WriteLine(
            $"User-defined name: " +
            $"{GetString(resource, "context", "user_defined_name") ?? "<missing>"}"
        );

        Console.WriteLine(
            $"Resource type: " +
            $"{GetString(resource, "context", "type") ?? "<missing>"}"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Aggregate status: " +
            $"{GetDisplayValue(resource, "aggregate", "status")}"
        );

        Console.WriteLine(
            $"Device state: " +
            $"{GetDisplayValue(resource, "aggregate", "running", "state")}"
        );

        Console.WriteLine(
            $"Plan: " +
            $"{GetDisplayValue(resource, "aggregate", "names")}"
        );

        Console.WriteLine();

        WriteBackupPolicies(
            resource
        );

        Console.WriteLine(
            new string(
                '-',
                48
            )
        );
    }

    private static void WriteBackupPolicies(
        JsonElement resource)
    {
        if (!TryGetProperty(
                resource,
                out var policies,
                "policies")
            || policies.ValueKind
                != JsonValueKind.Array)
        {
            Console.WriteLine(
                "Backup policy: <none>"
            );

            return;
        }

        var backupPolicies =
            policies
                .EnumerateArray()
                .Where(
                    policy =>
                    {
                        var type =
                            GetString(
                                policy,
                                "type"
                            );

                        return type is not null
                            && type.StartsWith(
                                "policy.backup.",
                                StringComparison.OrdinalIgnoreCase
                            );
                    }
                )
                .ToList();

        if (backupPolicies.Count == 0)
        {
            Console.WriteLine(
                "Backup policy: <none>"
            );

            return;
        }

        for (var index = 0;
             index < backupPolicies.Count;
             index++)
        {
            var policy =
                backupPolicies[index];

            Console.WriteLine(
                backupPolicies.Count > 1
                    ? $"Backup policy {index + 1}:"
                    : "Backup policy:"
            );

            Console.WriteLine(
                $"  Type: " +
                $"{GetDisplayValue(policy, "type")}"
            );

            Console.WriteLine(
                $"  Last run: " +
                $"{GetDisplayValue(policy, "last_run_time")}"
            );

            Console.WriteLine(
                $"  Last success: " +
                $"{GetDisplayValue(policy, "last_success_run_time")}"
            );

            Console.WriteLine(
                $"  Next run: " +
                $"{GetDisplayValue(policy, "next_run_time")}"
            );
        }
    }

    private static string? GetString(
        JsonElement root,
        params string[] path)
    {
        if (!TryGetProperty(
                root,
                out var value,
                path))
        {
            return null;
        }

        return value.ValueKind
            == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static string GetDisplayValue(
        JsonElement root,
        params string[] path)
    {
        if (!TryGetProperty(
                root,
                out var value,
                path))
        {
            return "<missing>";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String =>
                value.GetString()
                ?? "<null>",

            JsonValueKind.Null =>
                "<null>",

            JsonValueKind.Undefined =>
                "<missing>",

            _ =>
                value.GetRawText()
        };
    }

    private static bool TryGetProperty(
        JsonElement root,
        out JsonElement value,
        params string[] path)
    {
        value =
            root;

        foreach (var segment
                 in path)
        {
            if (value.ValueKind
                    != JsonValueKind.Object
                || !value.TryGetProperty(
                    segment,
                    out value))
            {
                value =
                    default;

                return false;
            }
        }

        return true;
    }
}