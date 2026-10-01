using System.Text.Json;
using SupportToolkit.Modules.BackupHealth.Models;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Modules.BackupHealth;

/// <summary>
/// Normalizes Acronis tenant, resource-status, and alert data into the
/// BackupHealth domain.
///
/// Individual malformed or uncorrelatable objects are surfaced as diagnostics
/// rather than silently discarded or allowed to fail the entire estate scan.
/// </summary>
public sealed class BackupHealthService
{
    private readonly IAcronisProvider _acronisProvider;

    public BackupHealthService(
        IAcronisProvider acronisProvider)
    {
        _acronisProvider = acronisProvider;
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
                tenant => tenant.Id,
                tenant => tenant
            );

        var alertsByResourceId =
            BuildAlertsByResourceId(
                alerts,
                diagnostics
            );

        var resources =
            new List<BackupResource>();

        foreach (var resourceStatus in resourceStatuses)
        {
            var context = resourceStatus.Context;

            var missingRequiredIdentity = false;

            if (string.IsNullOrWhiteSpace(context.Id))
            {
                diagnostics.Add(
                    new BackupHealthDiagnostic
                    {
                        Kind =
                            BackupHealthDiagnosticKind.ResourceMissingId,

                        Message =
                            $"Resource '{context.Name}' was skipped because " +
                            "Acronis did not provide a resource ID."
                    }
                );

                missingRequiredIdentity = true;
            }

            if (string.IsNullOrWhiteSpace(context.TenantId))
            {
                diagnostics.Add(
                    new BackupHealthDiagnostic
                    {
                        Kind =
                            BackupHealthDiagnosticKind.ResourceMissingTenantId,

                        Message =
                            $"Resource '{context.Name}' was skipped because " +
                            "Acronis did not provide a tenant ID."
                    }
                );

                missingRequiredIdentity = true;
            }

            if (missingRequiredIdentity)
            {
                continue;
            }

            var resourceId = context.Id!;
            var tenantId = context.TenantId!;

            string tenantName;

            if (tenantsById.TryGetValue(
                    tenantId,
                    out var tenant))
            {
                tenantName = tenant.Name;
            }
            else
            {
                tenantName = "Unknown tenant";

                diagnostics.Add(
                    new BackupHealthDiagnostic
                    {
                        Kind =
                            BackupHealthDiagnosticKind.ResourceUnknownTenant,

                        Message =
                            $"Resource '{context.Name}' references tenant " +
                            $"'{tenantId}', which was not present in the " +
                            "tenant response."
                    }
                );
            }

            /*
             * This currently assumes the first policy whose type starts with
             * "policy.backup" represents the backup state relevant to this
             * resource.
             *
             * That assumption should be validated against real production
             * Acronis responses once read-only API access is available.
             */
            var backupPolicy =
                resourceStatus.Policies?
                    .FirstOrDefault(
                        policy =>
                            policy.Type.StartsWith(
                                "policy.backup",
                                StringComparison.OrdinalIgnoreCase
                            )
                    );

            IReadOnlyList<BackupAlert> resourceAlerts =
                alertsByResourceId.TryGetValue(
                    resourceId,
                    out var matchedAlerts
                )
                    ? matchedAlerts
                    : Array.Empty<BackupAlert>();

            resources.Add(
                new BackupResource
                {
                    TenantId = tenantId,
                    TenantName = tenantName,
                    ResourceId = resourceId,
                    ResourceName = context.Name,
                    ResourceType = context.Type,
                    Status =
                        resourceStatus.Aggregate?.Status
                        ?? "unknown",
                    LastSuccessfulBackup =
                        backupPolicy?.LastSuccessRunTime,
                    Alerts = resourceAlerts
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
            Resources = resources.AsReadOnly(),
            Diagnostics = diagnostics.AsReadOnly()
        };
    }

    private static Dictionary<
        string,
        IReadOnlyList<BackupAlert>>
        BuildAlertsByResourceId(
            IReadOnlyList<AlertDto> alerts,
            ICollection<BackupHealthDiagnostic> diagnostics)
    {
        var alertsByResourceId =
            new Dictionary<string, List<BackupAlert>>();

        foreach (var alert in alerts)
        {
            var resourceId =
                GetResourceId(alert);

            if (string.IsNullOrWhiteSpace(resourceId))
            {
                diagnostics.Add(
                    new BackupHealthDiagnostic
                    {
                        Kind =
                            BackupHealthDiagnosticKind.AlertMissingResourceId,

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
                MapAlert(alert)
            );
        }

        return alertsByResourceId.ToDictionary(
            pair => pair.Key,
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
                .Select(resource => resource.ResourceId)
                .ToHashSet();

        foreach (var pair in alertsByResourceId)
        {
            if (usableResourceIds.Contains(pair.Key))
            {
                continue;
            }

            foreach (var alert in pair.Value)
            {
                diagnostics.Add(
                    new BackupHealthDiagnostic
                    {
                        Kind =
                            BackupHealthDiagnosticKind.AlertUnmatchedResource,

                        Message =
                            $"Alert '{alert.Id}' ({alert.Type}) references " +
                            $"resource '{pair.Key}', but no usable resource " +
                            "status was returned for that ID."
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
            Id = alert.Id,
            Type = alert.Type,
            Category = alert.Category,
            Severity = alert.Severity,
            CreatedAt = alert.CreatedAt
        };
    }
}