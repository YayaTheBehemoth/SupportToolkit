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

        var normalizer =
            new BackupInventoryNormalizer();

        var northwind =
            normalizer.BuildTenantReport(
                "Northwind Manufacturing",
                BuildNorthwindMicrosoft365Resources(
                    now
                ),
                BuildNorthwindDeviceResources(
                    now
                )
            );

        var contoso =
            normalizer.BuildTenantReport(
                "Contoso Workshop",
                [],
                BuildContosoDeviceResources(
                    now
                )
            );

        var alpine =
            normalizer.BuildTenantReport(
                "Alpine Consulting",
                BuildAlpineMicrosoft365Resources(
                    now
                ),
                BuildAlpineDeviceResources(
                    now
                )
            );

        var fabrikam =
            normalizer.BuildTenantReport(
                "Fabrikam Logistics",
                BuildFabrikamMicrosoft365Resources(
                    now
                ),
                BuildFabrikamDeviceResources(
                    now
                )
            );

        var adventureWorks =
            normalizer.BuildTenantReport(
                "Adventure Works",
                BuildAdventureWorksMicrosoft365Resources(
                    now
                ),
                BuildAdventureWorksDeviceResources(
                    now
                )
            );

        return new AggregatedBackupReport
        {
            Tenants =
            [
                northwind,
                contoso,
                alpine,
                fabrikam,
                adventureWorks
            ]
        };
    }

    private static IReadOnlyList<AcronisMicrosoft365ResourceDto>
        BuildNorthwindMicrosoft365Resources(
            DateTimeOffset now)
    {
        return
        [
            new AcronisMicrosoft365ResourceDto
            {
                Id = "northwind-mailbox-001",
                InternalId = "northwind-internal-mailbox-001",
                Name = "alex@northwind.example",
                Kind = "mailbox",
                ResourceType = "mailbox",
                HasProtections = true,
                LastTaskStatus = "ok",
                LastTaskState = "idle",
                LastSuccessTime = now.AddHours(-1),
                LastFinishTime = now.AddHours(-1)
            },

            new AcronisMicrosoft365ResourceDto
            {
                Id = "northwind-mailbox-002",
                InternalId = "northwind-internal-mailbox-002",
                Name = "finance@northwind.example",
                Kind = "mailbox",
                ResourceType = "mailbox",
                HasProtections = true,

                BasicKinds =
                [
                    new AcronisMicrosoft365BasicKindDto
                    {
                        Kind = "mailbox",
                        HasProtections = true
                    },

                    new AcronisMicrosoft365BasicKindDto
                    {
                        Kind = "calendar",
                        HasProtections = true
                    }
                ],

                LastTaskStatus = "ok",
                LastTaskState = "idle",
                LastSuccessTime = now.AddHours(-2),
                LastFinishTime = now.AddHours(-2)
            }
        ];
    }

    private static IReadOnlyList<AcronisDeviceResourceDto>
        BuildNorthwindDeviceResources(
            DateTimeOffset now)
    {
        return
        [
            new AcronisDeviceResourceDto
            {
                Id = "northwind-device-001",
                Name = "APP-SRV-01",

                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "idle",
                        LastBackup = now.AddHours(-2),
                        LastSuccessBackup = now.AddHours(-2),
                        NextBackup = now.AddHours(22),
                        AppliedPolicyNames = "Server Backup"
                    }
            },

            /*
             * Active backup with an established successful history.
             * This should be treated as normal operation and suppressed.
             */
            new AcronisDeviceResourceDto
            {
                Id = "northwind-device-002",
                Name = "DB-SRV-01",

                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "backup",
                        LastBackup = now.AddMinutes(-10),
                        LastSuccessBackup = now.AddDays(-1),
                        AppliedPolicyNames = "Database Backup"
                    }
            },

            /*
             * Acronis may normalize an active backup into "running".
             * Previous success means this is also normal operation.
             */
            new AcronisDeviceResourceDto
            {
                Id = "northwind-device-003",
                Name = "FILE-SRV-01",

                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "running",
                        LastBackup = now.AddMinutes(-5),
                        LastSuccessBackup = now.AddDays(-1),
                        AppliedPolicyNames = "File Server Backup"
                    }
            }
        ];
    }

    private static IReadOnlyList<AcronisDeviceResourceDto>
        BuildContosoDeviceResources(
            DateTimeOffset now)
    {
        return
        [
            /*
             * This tenant intentionally has no Microsoft 365 resources.
             * An empty M365 inventory is valid and should not make the
             * tenant incomplete.
             */
            new AcronisDeviceResourceDto
            {
                Id = "contoso-device-001",
                Name = "OFFICE-PC-01",

                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "ok",
                        LastBackup = now.AddHours(-4),
                        LastSuccessBackup = now.AddHours(-4),
                        NextBackup = now.AddHours(20),
                        AppliedPolicyNames = "Workstation Backup"
                    }
            }
        ];
    }

    private static IReadOnlyList<AcronisMicrosoft365ResourceDto>
        BuildAlpineMicrosoft365Resources(
            DateTimeOffset now)
    {
        return
        [
            /*
             * Conflicting protection signals must remain visible.
             */
            new AcronisMicrosoft365ResourceDto
            {
                Id = "alpine-mailbox-001",
                Name = "operations@alpine.example",
                Kind = "mailbox",
                ResourceType = "mailbox",
                HasProtections = true,

                BasicKinds =
                [
                    new AcronisMicrosoft365BasicKindDto
                    {
                        Kind = "mailbox",
                        HasProtections = true
                    },

                    new AcronisMicrosoft365BasicKindDto
                    {
                        Kind = "calendar",
                        HasProtections = false
                    }
                ],

                LastTaskStatus = "ok",
                LastTaskState = "idle",
                LastSuccessTime = now.AddHours(-3),
                LastFinishTime = now.AddHours(-3)
            },

            /*
             * Explicitly unprotected Microsoft 365 resource.
             */
            new AcronisMicrosoft365ResourceDto
            {
                Id = "alpine-mailbox-002",
                Name = "archive@alpine.example",
                Kind = "mailbox",
                ResourceType = "mailbox",
                HasProtections = false,
                LastTaskStatus = "ok",
                LastTaskState = "idle",
                LastSuccessTime = now.AddDays(-2),
                LastFinishTime = now.AddDays(-2)
            }
        ];
    }

    private static IReadOnlyList<AcronisDeviceResourceDto>
        BuildAlpineDeviceResources(
            DateTimeOffset now)
    {
        return
        [
            new AcronisDeviceResourceDto
            {
                Id = "alpine-device-001",
                Name = "EXAMPLE-SQL-01",

                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "notProtected",
                        LastBackup = now.AddDays(-3),
                        LastSuccessBackup = now.AddDays(-3),
                        AppliedPolicyNames = "SQL Backup (Disabled)"
                    }
            }
        ];
    }

    private static IReadOnlyList<AcronisMicrosoft365ResourceDto>
        BuildFabrikamMicrosoft365Resources(
            DateTimeOffset now)
    {
        return
        [
            /*
             * Protection exists, but no successful backup has been
             * established yet.
             */
            new AcronisMicrosoft365ResourceDto
            {
                Id = "fabrikam-mailbox-001",
                Name = "new.user@fabrikam.example",
                Kind = "mailbox",
                ResourceType = "mailbox",
                HasProtections = true,
                LastTaskStatus = "ok",
                LastTaskState = "idle",
                LastSuccessTime = null,
                LastFinishTime = now.AddMinutes(-30)
            }
        ];
    }

    private static IReadOnlyList<AcronisDeviceResourceDto>
        BuildFabrikamDeviceResources(
            DateTimeOffset now)
    {
        return
        [
            /*
             * Active backup, but there is no previous successful backup.
             * Keep it visible until the first success is established.
             */
            new AcronisDeviceResourceDto
            {
                Id = "fabrikam-device-001",
                Name = "NEW-SRV-01",

                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "running",
                        LastBackup = now.AddMinutes(-15),
                        LastSuccessBackup = null,
                        AppliedPolicyNames = "Server Backup"
                    }
            },

            /*
             * Backup is scheduled but has never run successfully.
             */
            new AcronisDeviceResourceDto
            {
                Id = "fabrikam-device-002",
                Name = "LAPTOP-01",

                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "idle",
                        LastBackup = null,
                        LastSuccessBackup = null,
                        NextBackup = now.AddHours(2),
                        AppliedPolicyNames = "Endpoint Backup"
                    }
            }
        ];
    }

    private static IReadOnlyList<AcronisMicrosoft365ResourceDto>
        BuildAdventureWorksMicrosoft365Resources(
            DateTimeOffset now)
    {
        return
        [
            /*
             * Otherwise healthy-looking data with no usable resource name.
             * The normalizer must preserve this as Unknown.
             */
            new AcronisMicrosoft365ResourceDto
            {
                Id = "adventure-mailbox-001",
                Name = null,
                Kind = "mailbox",
                ResourceType = "mailbox",
                HasProtections = true,
                LastTaskStatus = "ok",
                LastTaskState = "idle",
                LastSuccessTime = now.AddHours(-1),
                LastFinishTime = now.AddHours(-1)
            }
        ];
    }

    private static IReadOnlyList<AcronisDeviceResourceDto>
        BuildAdventureWorksDeviceResources(
            DateTimeOffset now)
    {
        return
        [
            /*
             * Unknown future vendor states must never be silently treated
             * as healthy.
             */
            new AcronisDeviceResourceDto
            {
                Id = "adventure-device-001",
                Name = "FUTURE-DEVICE-01",

                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "future_vendor_state",
                        LastBackup = now.AddHours(-6),
                        LastSuccessBackup = now.AddHours(-6),
                        AppliedPolicyNames = "Example Backup"
                    }
            },

            /*
             * Idle with evidence of a previous attempt, but no successful
             * backup. There is not enough evidence to call this healthy.
             */
            new AcronisDeviceResourceDto
            {
                Id = "adventure-device-002",
                Name = "UNCERTAIN-PC-01",

                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "idle",
                        LastBackup = now.AddHours(-8),
                        LastSuccessBackup = null,
                        AppliedPolicyNames = "Endpoint Backup"
                    }
            }
        ];
    }
}