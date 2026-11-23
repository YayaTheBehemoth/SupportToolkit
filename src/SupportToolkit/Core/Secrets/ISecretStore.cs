namespace SupportToolkit.Core.Secrets;

/// <summary>
/// Provider-independent abstraction for persistent secret storage.
///
/// Application code must depend on this contract rather than directly on
/// Windows Credential Manager or any future centralized secret service.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetSecretAsync(
        string key,
        CancellationToken cancellationToken = default
    );

    Task SetSecretAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default
    );

    Task RemoveSecretAsync(
        string key,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// False when this store only reads secrets that are set elsewhere,
    /// such as environment variables; callers must then not ask for a
    /// secret to store.
    /// </summary>
    bool CanStoreSecrets => true;

    /// <summary>What the secrets are kept in, for status output.</summary>
    string Description => "the secret store of this computer";

    /// <summary>
    /// Where a secret is set instead of stored (an environment variable's
    /// name), or null when the store keeps secrets itself.
    /// </summary>
    string? ExternalLocation(
        string key) =>
        null;
}