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
}