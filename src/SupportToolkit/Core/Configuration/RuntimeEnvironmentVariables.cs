namespace SupportToolkit.Core.Configuration;

/// <summary>
/// The environment variables that can override saved configuration, and the
/// obsolete ones that are ignored. One list, shared by every interface that
/// reports configuration status, so they cannot drift apart.
/// </summary>
public static class RuntimeEnvironmentVariables
{
    public static readonly IReadOnlyList<string> Overrides =
    [
        "SUPPORTTOOLKIT_CONFIG",
        "SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE",
        "ACRONIS_DATACENTER_URL",
        "ACRONIS_CLIENT_ID",
        "ACRONIS_CLIENT_SECRET",
        "ZENDESK_SUBDOMAIN",
        "ZENDESK_CLIENT_ID",
        "ZENDESK_CLIENT_SECRET"
    ];

    public static readonly IReadOnlyList<string> Obsolete =
    [
        "SUPPORTTOOLKIT_ALLOW_WRITES",
        "SUPPORTTOOLKIT_BACKUP_HEALTH_MODE"
    ];
}
