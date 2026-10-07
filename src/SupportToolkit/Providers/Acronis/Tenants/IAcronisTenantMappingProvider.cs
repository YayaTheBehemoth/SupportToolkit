namespace SupportToolkit.Providers.Acronis.Tenants;

/// <summary>
/// Provides mappings between legacy numeric Acronis tenant identifiers and
/// the UUID identifiers used by Account Management API v2.
/// </summary>
public interface IAcronisTenantMappingProvider
{
    Task<IReadOnlyDictionary<string, string>>
        GetTenantIdMappingsAsync(
            CancellationToken cancellationToken = default);
}
