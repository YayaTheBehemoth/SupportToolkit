namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Persistent, non-secret SupportToolkit configuration.
///
/// Connection metadata is defined independently from runtime profiles so the
/// same external connection can be reused by multiple profiles.
///
/// Authentication secrets deliberately do not exist in this model.
/// Secrets belong in ISecretStore.
/// </summary>
public sealed class SupportToolkitConfiguration
{
    public string ActiveProfile { get; set; } =
        "local-dev";

    public SupportToolkitConnections Connections { get; set; } =
        new();

    public Dictionary<string, SupportToolkitProfile> Profiles { get; set; } =
        new();
}

/// <summary>
/// Named external connections available to SupportToolkit profiles.
/// </summary>
public sealed class SupportToolkitConnections
{
    public Dictionary<string, AcronisConnectionConfiguration> Acronis
    {
        get;
        set;
    } =
        new();

    public Dictionary<string, ZendeskConnectionConfiguration> Zendesk
    {
        get;
        set;
    } =
        new();
}

/// <summary>
/// Defines one runtime profile.
///
/// Module modes control where individual modules obtain their operational
/// data.
///
/// Connection references are independent from module modes. This allows, for
/// example, BackupAggregator to use fixture data while ticket submission uses
/// a configured Zendesk sandbox.
///
/// External writes are disabled by default and must be enabled explicitly by
/// the active profile or an environment override.
/// </summary>
public sealed class SupportToolkitProfile
{
    public string? AcronisConnection { get; set; }

    public string? ZendeskConnection { get; set; }

    public bool AllowWrites { get; set; } =
        false;

    public Dictionary<string, SupportToolkitMode> Modules { get; set; } =
        new();
}

/// <summary>
/// Non-secret Acronis connection metadata.
///
/// ClientSecret intentionally does not exist here.
/// </summary>
public sealed class AcronisConnectionConfiguration
{
    public string DatacenterUrl { get; set; } =
        string.Empty;

    public string ClientId { get; set; } =
        string.Empty;
}

/// <summary>
/// Non-secret Zendesk connection metadata.
///
/// ClientSecret intentionally does not exist here.
/// </summary>
public sealed class ZendeskConnectionConfiguration
{
    public string Subdomain { get; set; } =
        string.Empty;

    public string ClientId { get; set; } =
        string.Empty;
}