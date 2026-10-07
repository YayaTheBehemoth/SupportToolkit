using System.Text.Json;
using SupportToolkit.Modules.BackupHealth.Models;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Alerts.Dtos;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
namespace SupportToolkit.Modules.BackupHealth;

/// <summary>
/// Normalizes Acronis tenant, resource-status, and alert data into the
/// BackupHealth domain.
///
/// Structural Acronis resource-group objects are excluded from BackupHealth,
/// while malformed or uncorrelatable workload data is surfaced through
/// diagnostics rather than silently discarded.
/// </summary>
public sealed class BackupHealthService
{
    private const string ResourceGroupPrefix =
        "resource.group.";

    private readonly IAcronisProvider _acronisProvider;

    public BackupHealthService(
        IAcronisProvider acronisProvider)
    {
        _acronisProvider =
            acronisProvider;
    }

    public async Task<BackupHealthSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var tenants =
            await _acronisProvider.GetTenantsAsync(
                cancellationToken
            );

        var resourceStatuses =
            await _acronisProvider.GetResourceStatusesAsync(
                cancellationToken
            );

        var alerts =
            await _acronisProvider.GetAlertsAsync(
                cancellationToken
            );

        var diagnostics =
            new List<BackupHealthDiagnostic>();

        var tenantsById =
            tenants.ToDictionary(
                tenant =>
                    tenant.Id,

                tenant =>
                    tenant,

                StringComparer.OrdinalIgnoreCase
            );

        /*
         * resource_statuses also contains hierarchy/navigation objects.
         *
         * These are not independently protected workloads and must not
         * participate in BackupHealth evaluation.
         */
        var workloadStatuses =
            resourceStatuses
                .Where(
                    resourceStatus =>
                        !IsStructuralResourceGroup(
                            resourceStatus.Context.Type
                        )
                )
                .ToList();

        IReadOnlyDictionary<string, string>
            tenantIdMappings =
                new Dictionary<string, string>();

        /*
         * Older Acronis API surfaces may return numeric tenant identifiers,
         * while Account Management v2 uses UUIDs.
         *
         * Production providers can expose a complete hierarchy-derived
         * numeric-to-UUID mapping without BackupHealth needing to know how
         * that mapping is obtained.
         */
        if (workloadStatuses.Any(
                resource =>
                    IsPositiveNumericTenantId(
                        resource.Context.TenantId
                    ))
            && _acronisProvider
                is IAcronisTenantMappingProvider mappingProvider)
        {
            tenantIdMappings =
                await mappingProvider
                    .GetTenantIdMappingsAsync(
                        cancellationToken
                    );
        }

        var alertsByResourceId =
            BuildAlertsByResourceId(
                alerts,
                diagnostics
            );

        var resources =
            new List<BackupResource>();

        foreach (var resourceStatus
                 in workloadStatuses)
        {
            var context =
                resourceStatus.Context;

            var missingRequiredIdentity =
                false;

            if (string.IsNullOrWhiteSpace(
                    context.Id))
            {
                diagnostics.Add(
                    new BackupHealthDiagnostic
                    {
                        Kind =
                            BackupHealthDiagnosticKind
                                .ResourceMissingId,

                        Message =
                            $"Resource '{context.Name}' was skipped because " +
                            "Acronis did not provide a resource ID."
                    }
                );

                missingRequiredIdentity =
                    true;
            }

            if (string.IsNullOrWhiteSpace(
                    context.TenantId))
            {
                diagnostics.Add(
                    new BackupHealthDiagnostic
                    {
                        Kind =
                            BackupHealthDiagnosticKind
                                .ResourceMissingTenantId,

                        Message =
                            $"Resource '{context.Name}' was skipped because " +
                            "Acronis did not provide a tenant ID."
                    }
                );

                missingRequiredIdentity =
                    true;
            }

            if (missingRequiredIdentity)
            {
                continue;
            }

            var resourceId =
                context.Id!;

            var externalTenantId =
                context.TenantId!;

            var normalizedTenantId =
                externalTenantId;

            TenantDto? tenant =
                null;

            /*
             * First attempt a direct v2 UUID join.
             */
            if (tenantsById.TryGetValue(
                    externalTenantId,
                    out var directTenant))
            {
                tenant =
                    directTenant;

                normalizedTenantId =
                    directTenant.Id;
            }
            /*
             * Otherwise translate a legacy numeric ID through the hierarchy
             * map and join the resulting UUID against Account Management v2.
             */
            else if (tenantIdMappings.TryGetValue(
                         externalTenantId,
                         out var mappedUuid))
            {
                normalizedTenantId =
                    mappedUuid;

                tenantsById.TryGetValue(
                    mappedUuid,
                    out tenant
                );
            }

            string tenantName;

            if (tenant is not null)
            {
                tenantName =
                    tenant.Name;
            }
            else
            {
                tenantName =
                    "Unknown tenant";

                diagnostics.Add(
                    new BackupHealthDiagnostic
                    {
                        Kind =
                            BackupHealthDiagnosticKind
                                .ResourceUnknownTenant,

                        Message =
                            $"Resource '{context.Name}' references a tenant " +
                            "that could not be correlated with the Account " +
                            "Management tenant hierarchy."
                    }
                );
            }

            var backupPolicy =
                resourceStatus.Policies?
                    .FirstOrDefault(
                        policy =>
                            policy.Type.StartsWith(
                                "policy.backup",
                                StringComparison.OrdinalIgnoreCase
                            )
                    );

            IReadOnlyList<BackupAlert>
                resourceAlerts =
                    alertsByResourceId.TryGetValue(
                        resourceId,
                        out var matchedAlerts
                    )
                        ? matchedAlerts
                        : Array.Empty<BackupAlert>();

            resources.Add(
                new BackupResource
                {
                    TenantId =
                        normalizedTenantId,

                    TenantName =
                        tenantName,

                    ResourceId =
                        resourceId,

                    ResourceName =
                        context.Name,

                    ResourceType =
                        context.Type,

                    Status =
                        resourceStatus.Aggregate?.Status
                        ?? "unknown",

                    LastSuccessfulBackup =
                        backupPolicy?.LastSuccessRunTime,

                    Alerts =
                        resourceAlerts
                }
            );
        }

        RecordUnmatchedAlerts(
            alertsByResourceId,
            resources,
            diagnostics
        );

        return new BackupHealthSnapshot
        {
            Resources =
                resources.AsReadOnly(),

            Diagnostics =
                diagnostics.AsReadOnly()
        };
    }

    private static bool IsStructuralResourceGroup(
        string resourceType)
    {
        return resourceType.StartsWith(
            ResourceGroupPrefix,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsPositiveNumericTenantId(
        string? tenantId)
    {
        return long.TryParse(
                   tenantId,
                   out var numericTenantId)
               && numericTenantId > 0;
    }

    private static Dictionary<
        string,
        IReadOnlyList<BackupAlert>>
        BuildAlertsByResourceId(
            IReadOnlyList<AlertDto> alerts,
            ICollection<BackupHealthDiagnostic> diagnostics)
    {
        var alertsByResourceId =
            new Dictionary<
                string,
                List<BackupAlert>>();

        foreach (var alert in alerts)
        {
            var resourceId =
                GetResourceId(
                    alert
                );

            if (string.IsNullOrWhiteSpace(
                    resourceId))
            {
                diagnostics.Add(
                    new BackupHealthDiagnostic
                    {
                        Kind =
                            BackupHealthDiagnosticKind
                                .AlertMissingResourceId,

                        Message =
                            $"Alert '{alert.Id}' ({alert.Type}) could not be " +
                            "correlated because its details did not contain " +
                            "a usable resourceId."
                    }
                );

                continue;
            }

            if (!alertsByResourceId.TryGetValue(
                    resourceId,
                    out var resourceAlerts))
            {
                resourceAlerts =
                    [];

                alertsByResourceId.Add(
                    resourceId,
                    resourceAlerts
                );
            }

            resourceAlerts.Add(
                MapAlert(
                    alert
                )
            );
        }

        return alertsByResourceId
            .ToDictionary(
                pair =>
                    pair.Key,

                pair =>
                    (IReadOnlyList<BackupAlert>)
                    pair.Value.AsReadOnly()
            );
    }

    private static void RecordUnmatchedAlerts(
        IReadOnlyDictionary<
            string,
            IReadOnlyList<BackupAlert>> alertsByResourceId,
        IReadOnlyList<BackupResource> resources,
        ICollection<BackupHealthDiagnostic> diagnostics)
    {
        var usableResourceIds =
            resources
                .Select(
                    resource =>
                        resource.ResourceId
                )
                .ToHashSet();

        foreach (var pair in alertsByResourceId)
        {
            if (usableResourceIds.Contains(
                    pair.Key))
            {
                continue;
            }

            foreach (var alert in pair.Value)
            {
                diagnostics.Add(
                    new BackupHealthDiagnostic
                    {
                        Kind =
                            BackupHealthDiagnosticKind
                                .AlertUnmatchedResource,

                        Message =
                            $"Alert '{alert.Id}' ({alert.Type}) references " +
                            "a resource that was not present in the normalized " +
                            "BackupHealth workload set."
                    }
                );
            }
        }
    }

    private static string? GetResourceId(
        AlertDto alert)
    {
        if (alert.Details.ValueKind
            != JsonValueKind.Object)
        {
            return null;
        }

        if (!alert.Details.TryGetProperty(
                "resourceId",
                out var resourceId))
        {
            return null;
        }

        return resourceId.ValueKind
            == JsonValueKind.String
                ? resourceId.GetString()
                : null;
    }

    private static BackupAlert MapAlert(
        AlertDto alert)
    {
        return new BackupAlert
        {
            Id =
                alert.Id,

            Type =
                alert.Type,

            Category =
                alert.Category,

            Severity =
                alert.Severity,

            CreatedAt =
                alert.CreatedAt
        };
    }
}
