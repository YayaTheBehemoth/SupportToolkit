namespace SupportToolkit.Core.Secrets;

/// <summary>
/// Reads SupportToolkit secrets from environment variables, for systems
/// without a supported secret store (Linux, a container, a codespace).
///
/// It never stores anything: a secret written to disk in plain text is not
/// an option. Setting or removing a secret fails and names the variable to
/// set instead, for example as a Codespaces secret.
///
/// A key "provider:connection:secret" is read from
/// SUPPORTTOOLKIT_SECRET__PROVIDER__CONNECTION__SECRET: upper case, with every
/// character other than A-Z and 0-9 written as an underscore. For example
/// zendesk:trial:client-secret is read from
/// SUPPORTTOOLKIT_SECRET__ZENDESK__TRIAL__CLIENT_SECRET.
///
/// Connection names may contain '-', '_' and '.', which all become '_', so
/// two connections of one provider can map to the same variable (prod-eu and
/// prod_eu). The configuration resolver refuses such a clash rather than
/// sending one connection's secret to the other.
/// </summary>
public sealed class EnvironmentSecretStore
    : ISecretStore
{
    public const string VariablePrefix =
        "SUPPORTTOOLKIT_SECRET__";

    private readonly Func<string, string?> _environmentReader;

    public EnvironmentSecretStore(
        Func<string, string?>? environmentReader = null)
    {
        _environmentReader =
            environmentReader
            ?? Environment.GetEnvironmentVariable;
    }

    public bool CanStoreSecrets => false;

    public string Description =>
        "environment variables (" + VariablePrefix + "*)";

    public string? ExternalLocation(
        string key) =>
        VariableName(key);

    /// <summary>
    /// The environment variable a secret key is read from.
    /// </summary>
    public static string VariableName(
        string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException(
                "Secret key cannot be empty.",
                nameof(key));
        }

        var parts = key.Trim().Split(':');

        if (parts.Length != 3
            || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Secret key must have the form provider:connection:secret.",
                nameof(key));
        }

        return VariablePrefix
               + string.Join(
                   "__",
                   parts.Select(Normalize));
    }

    public Task<string?> GetSecretAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value =
            _environmentReader(
                VariableName(key));

        return Task.FromResult(
            string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim());
    }

    public Task SetSecretAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default) =>
        throw NotStored(key);

    public Task RemoveSecretAsync(
        string key,
        CancellationToken cancellationToken = default) =>
        throw NotStored(key);

    // The message names the variable only; a value is never part of it.
    private static InvalidOperationException NotStored(
        string key) =>
        new(
            "Secrets are not stored by SupportToolkit on this system. " +
            $"Set the environment variable {VariableName(key)} instead " +
            "(for example as a Codespaces secret).");

    private static string Normalize(
        string part) =>
        new(
            part
                .Trim()
                .ToUpperInvariant()
                .Select(
                    character =>
                        character is >= 'A' and <= 'Z' or >= '0' and <= '9'
                            ? character
                            : '_')
                .ToArray());
}
