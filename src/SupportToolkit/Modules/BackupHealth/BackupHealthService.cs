using System.Text.Json;
using SupportToolkit.Modules.BackupHealth.Models;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Modules.BackupHealth;

public sealed class BackupHealthService
{
    private readonly IAcronisProvider _acronisProvider;

    public BackupHealthService(IAcronisProvider acronisProvider)
    {
        _acronisProvider = acronisProvider;
    }

    public async Task<IReadOnlyList<BackupResource>> GetBackupResourcesAsync(
        CancellationToken cancellationToken = default)
    {
        var tenants = await _acronisProvider.GetTenantsAsync(
            cancellationToken
        );

        var resourceStatuses =
            await _acronisProvider.GetResourceStatusesAsync(
                cancellationToken
            );

        var alerts = await _acronisProvider.GetAlertsAsync(
            cancellationToken
        );

        var tenantsById = tenants.ToDictionary(
            tenant => tenant.Id,
            tenant => tenant
        );

        var alertsByResourceId = alerts
            .Select(alert => new
            {
                Alert = alert,
                ResourceId = GetResourceId(alert)
            })
            .Where(item => item.ResourceId is not null)
            .GroupBy(item => item.ResourceId!)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => MapAlert(item.Alert))
                    .ToList()
                    .AsReadOnly()
            );

        var resources = new List<BackupResource>();

        foreach (var resourceStatus in resourceStatuses)
        {
            var context = resourceStatus.Context;

            if (context.Id is null || context.TenantId is null)
            {
                continue;
            }

            var tenantName =
                tenantsById.TryGetValue(context.TenantId, out var tenant)
                    ? tenant.Name
                    : "Unknown tenant";

            var backupPolicy = resourceStatus.Policies?
                .FirstOrDefault(policy =>
                    policy.Type.StartsWith("policy.backup")
                );

     IReadOnlyList<BackupAlert> resourceAlerts =
    alertsByResourceId.TryGetValue(
        context.Id,
        out var matchedAlerts
    )
        ? matchedAlerts
        : Array.Empty<BackupAlert>();

            resources.Add(new BackupResource
            {
                TenantId = context.TenantId,
                TenantName = tenantName,
                ResourceId = context.Id,
                ResourceName = context.Name,
                ResourceType = context.Type,
                Status = resourceStatus.Aggregate?.Status ?? "unknown",
                LastSuccessfulBackup =
                    backupPolicy?.LastSuccessRunTime,
                Alerts = resourceAlerts
            });
        }

        return resources;
    }

    private static string? GetResourceId(AlertDto alert)
    {
        if (alert.Details.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!alert.Details.TryGetProperty(
                "resourceId",
                out var resourceId))
        {
            return null;
        }

        return resourceId.GetString();
    }

    private static BackupAlert MapAlert(AlertDto alert)
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