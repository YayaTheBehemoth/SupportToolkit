using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Composition;

/// <summary>
/// Chooses the secret store for this system. Windows keeps secrets in the
/// Windows Credential Manager; every other system reads them from
/// environment variables and stores none.
/// </summary>
public static class SecretStoreComposition
{
    public static ISecretStore Create(
        Func<bool>? isWindows = null,
        Func<string, string?>? environmentReader = null)
    {
        var windows =
            (isWindows ?? OperatingSystem.IsWindows)();

        return windows
            ? new WindowsCredentialStore()
            : new EnvironmentSecretStore(environmentReader);
    }
}
