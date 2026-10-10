namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Persistent, non-secret SupportToolkit configuration.
///
/// This model deliberately contains no authentication secrets.
/// Secrets belong in ISecretStore.
/// </summary>
public sealed class SupportToolkitConfiguration
{
    public string ActiveProfile { get; set; } =
        "development";

    public Dictionary<string, SupportToolkitProfile> Profiles { get; set; } =
        new();
}

/// <summary>
/// Non-secret configuration belonging to one named SupportToolkit profile.
/// </summary>
public sealed class SupportToolkitProfile
{
    public AcronisProfileConfiguration Acronis { get; set; } =
        new();

    public ZendeskProfileConfiguration Zendesk { get; set; } =
        new();

    public Dictionary<string, SupportToolkitMode> Modules { get; set; } =
        new();
}

/// <summary>
/// Non-secret Acronis connection metadata.
///
/// ClientSecret intentionally does not exist here.
/// </summary>
public sealed class AcronisProfileConfiguration
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
public sealed class ZendeskProfileConfiguration
{
    public string Subdomain { get; set; } =
        string.Empty;

    public string ClientId { get; set; } =
        string.Empty;
}