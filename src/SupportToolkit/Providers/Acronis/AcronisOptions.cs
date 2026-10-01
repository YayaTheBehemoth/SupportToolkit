namespace SupportToolkit.Providers.Acronis;

/// Represents the runtime configuration required to authenticate against an Acronis data center.

public sealed class AcronisOptions
{
    public required string DatacenterUrl { get; init; }

    public required string ClientId { get; init; }

 
    public required string ClientSecret { get; init; }
}