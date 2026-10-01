namespace SupportToolkit.Providers.Acronis;

public sealed class AcronisOptions
{
    public required string DatacenterUrl { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }
}