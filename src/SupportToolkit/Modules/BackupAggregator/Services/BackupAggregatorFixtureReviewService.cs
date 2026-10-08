using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class BackupAggregatorFixtureReviewService
{
    public AggregatedBackupReport BuildReport()
    {
        var now =
            new DateTimeOffset(
                2026,
                10,
                8,
                12,
                0,
                0,
                TimeSpan.Zero
            );

        var microsoft365Resources =
            new[]
            {
                new AcronisMicrosoft365ResourceDto
                {
                    Id = "fixture-mailbox-1",
                    Name = "Fixture mailbox",
                    HasProtections = true,
                    LastTaskStatus = "ok",
                    LastTaskState = "idle",
                    LastSuccessTime = now.AddHours(-1),
                    LastFinishTime = now.AddHours(-1)
                }
            };

        var deviceResources =
            new[]
            {
                new AcronisDeviceResourceDto
                {
                    Id = "fixture-device-1",
                    Name = "FIXTURE-SRV",
                    Status =
                        new AcronisDeviceResourceStatusDto
                        {
                            State = "idle",
                            LastBackup = now.AddHours(-2),
                            LastSuccessBackup = now.AddHours(-2),
                            AppliedPolicyNames = "Server Backup"
                        }
                },

                new AcronisDeviceResourceDto
                {
                    Id = "fixture-device-2",
                    Name = "FIXTURE-SQL",
                    Status =
                        new AcronisDeviceResourceStatusDto
                        {
                            State = "notProtected",
                            LastBackup = now.AddDays(-1),
                            LastSuccessBackup = now.AddDays(-1),
                            AppliedPolicyNames = "SQL Backup (Disabled)"
                        }
                }
            };

        var tenantReport =
            new BackupInventoryNormalizer()
                .BuildTenantReport(
                    "Fixture Tenant",
                    microsoft365Resources,
                    deviceResources
                );

        return new AggregatedBackupReport
        {
            Tenants = [tenantReport]
        };
    }
}
