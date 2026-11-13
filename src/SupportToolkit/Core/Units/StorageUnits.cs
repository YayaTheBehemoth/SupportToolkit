namespace SupportToolkit.Core.Units;

/// <summary>
/// Storage units, defined once. Business Central bills backup storage
/// in decimal gigabytes; the Acronis portal shows binary gigabytes
/// (GiB) while labelling them GB.
/// </summary>
public static class StorageUnits
{
    /// <summary>Decimal gigabyte: 1,000,000,000 bytes.</summary>
    public const decimal BytesPerGb = 1_000_000_000m;

    /// <summary>Binary gigabyte (GiB): 1,073,741,824 bytes.</summary>
    public const decimal BytesPerGib = 1_073_741_824m;
}
