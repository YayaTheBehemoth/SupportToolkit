using SupportToolkit.Core.Configuration;
using SupportToolkit.Providers.Zendesk.Deletion;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Setup and status for the Zendesk deletion connection.
///
/// This is a separate connection from the Zendesk ticketing connection,
/// with its own OAuth client and its own profile reference. It is a public
/// client that signs in with the operator's browser, so no secret is asked
/// for or stored, and no destination or user IDs exist. Both connections
/// may point at the same Zendesk subdomain, but are configured and shown
/// independently.
/// </summary>
public sealed partial class ConfigureCommand
{
    /// <summary>
    /// Keeps a valid existing Zendesk deletion connection, or returns
    /// false when setup is required.
    /// </summary>
    private static bool KeepExistingZendeskDeletionConnection(
        SupportToolkitConfiguration configuration,
        SupportToolkitProfile profile)
    {
        if (string.IsNullOrWhiteSpace(
                profile.ZendeskDeletionConnection))
        {
            return false;
        }

        var connectionName =
            profile.ZendeskDeletionConnection;

        var connection =
            FindZendeskDeletionConnection(
                configuration,
                connectionName);

        if (connection is null)
        {
            Console.WriteLine(
                $"Zendesk deletion connection '{connectionName}' " +
                "no longer exists.");

            Console.WriteLine("Connection setup is required.");

            return false;
        }

        if (!TryCreateZendeskDeletionOptions(
                connection,
                out var options,
                out _))
        {
            Console.WriteLine(
                $"Zendesk deletion connection '{connectionName}' " +
                "is incomplete or invalid.");

            Console.WriteLine("Connection setup is required.");

            return false;
        }

        Console.WriteLine("Existing Zendesk deletion configuration");
        Console.WriteLine("---------------------------------------");
        Console.WriteLine($"Connection: {connectionName}");
        Console.WriteLine($"Instance:   {options!.Subdomain}");
        Console.WriteLine("Sign-in:    interactive, per run");
        Console.WriteLine();

        var keep =
            ReadConfirmation(
                "Keep existing Zendesk deletion configuration? [Y/n]: ",
                defaultValue: true);

        if (keep)
        {
            Console.WriteLine(
                "Zendesk deletion configuration kept unchanged.");
        }

        return keep;
    }

    private static void ConfigureZendeskDeletionConnection(
        SupportToolkitConfiguration configuration,
        SupportToolkitProfile profile)
    {
        Console.WriteLine("Zendesk deletion connection");
        Console.WriteLine("---------------------------");
        Console.WriteLine(
            "Interactive bulk ticket deletion. This is separate from the");
        Console.WriteLine(
            "Zendesk ticketing connection: it uses its own public OAuth");
        Console.WriteLine(
            "client and signs the operator in through the browser on each");
        Console.WriteLine(
            "run. No secret or token is ever stored by SupportToolkit.");
        Console.WriteLine();

        var connectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    ReadRequiredWithDefault(
                        "Connection name",
                        GetDefaultConnectionName(
                            profile.ZendeskDeletionConnection,
                            configuration.Connections
                                .ZendeskDeletion.Keys)));

        profile.ZendeskDeletionConnection =
            connectionName;

        var connection =
            FindZendeskDeletionConnection(
                configuration,
                connectionName)
            ?? new ZendeskDeletionConnectionConfiguration();

        while (true)
        {
            connection.Subdomain =
                ReadRequiredWithDefault(
                    "Zendesk subdomain",
                    connection.Subdomain);

            connection.ClientId =
                ReadRequiredWithDefault(
                    "Public OAuth client identifier",
                    connection.ClientId);

            if (TryCreateZendeskDeletionOptions(
                    connection,
                    out _,
                    out var problem))
            {
                break;
            }

            Console.WriteLine();
            Console.WriteLine($"Invalid value: {problem}");
            Console.WriteLine("Please re-enter the connection details.");
            Console.WriteLine();
        }

        RemoveKeyIgnoringCase(
            configuration.Connections.ZendeskDeletion,
            connectionName);

        configuration.Connections.ZendeskDeletion[connectionName] =
            connection;
    }

    private static bool TryCreateZendeskDeletionOptions(
        ZendeskDeletionConnectionConfiguration connection,
        out ZendeskDeletionOptions? options,
        out string? problem)
    {
        try
        {
            options =
                ZendeskDeletionOptions.Create(
                    connection.Subdomain,
                    connection.ClientId);

            problem = null;

            return true;
        }
        catch (InvalidOperationException exception)
        {
            options = null;
            problem = exception.Message;

            return false;
        }
    }

    private static ZendeskDeletionConnectionConfiguration?
        FindZendeskDeletionConnection(
            SupportToolkitConfiguration configuration,
            string connectionName)
    {
        return configuration.Connections.ZendeskDeletion
            .FirstOrDefault(
                pair =>
                    string.Equals(
                        pair.Key,
                        connectionName,
                        StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static async Task WriteEffectiveZendeskDeletionAsync(
        SupportToolkitProfile profile,
        SupportToolkitConfigurationResolver resolver)
    {
        Console.WriteLine("Zendesk deletion (separate from ticketing)");

        if (string.IsNullOrWhiteSpace(
                profile.ZendeskDeletionConnection))
        {
            Console.WriteLine("  Connection: not selected");

            Console.WriteLine(
                "  Bulk deletion: unavailable until a connection is " +
                "configured.");

            return;
        }

        Console.WriteLine(
            $"  Connection alias: {profile.ZendeskDeletionConnection}");

        try
        {
            var resolved =
                await resolver.GetZendeskDeletionConnectionAsync();

            var options =
                ZendeskDeletionOptions.Create(
                    resolved.Subdomain,
                    resolved.ClientId);

            Console.WriteLine($"  Instance: {options.Subdomain}");

            Console.WriteLine(
                "  Sign-in: interactive per run (nothing stored)");

            Console.WriteLine(
                "  Environment overrides: none (saved connection only)");

            Console.WriteLine("  Configuration: valid locally");
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine(
                "  Configuration: incomplete or invalid");

            Console.WriteLine(
                $"  Reason: {exception.Message}");
        }
    }

    private static void WriteSavedZendeskDeletionConnections(
        SupportToolkitConfiguration configuration)
    {
        Console.WriteLine("Saved Zendesk deletion connections");
        Console.WriteLine("----------------------------------");
        Console.WriteLine();

        if (configuration.Connections.ZendeskDeletion.Count == 0)
        {
            Console.WriteLine(
                "No Zendesk deletion connections configured.");
        }

        foreach (var pair in configuration.Connections.ZendeskDeletion
                     .OrderBy(
                         pair => pair.Key,
                         StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(pair.Key);

            Console.WriteLine(
                $"    Instance:      {DisplayValue(pair.Value.Subdomain)}");

            Console.WriteLine(
                $"    Client ID:     {DisplaySecretStatus(
                    !string.IsNullOrWhiteSpace(pair.Value.ClientId))}");

            Console.WriteLine(
                "    Client secret: none (public client)");
        }

        Console.WriteLine();
    }
}
