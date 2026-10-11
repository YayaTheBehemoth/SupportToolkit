using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Interactive operator command for configuring SupportToolkit.
///
/// Profiles define runtime behavior.
/// Connections define reusable external provider endpoints.
/// Authentication secrets are stored separately through ISecretStore.
///
/// This is a host-level command rather than an operational module.
/// </summary>
public sealed partial class ConfigureCommand
{
    private const string BackupAggregatorCommand =
        "backup-aggregator";

    private const string BackupHealthCommand =
        "backup-health";

    private readonly ISupportToolkitConfigurationStore
        _configurationStore;

    private readonly ISecretStore
        _secretStore;

    public ConfigureCommand(
        ISupportToolkitConfigurationStore configurationStore,
        ISecretStore secretStore)
    {
        _configurationStore =
            configurationStore
            ?? throw new ArgumentNullException(
                nameof(configurationStore)
            );

        _secretStore =
            secretStore
            ?? throw new ArgumentNullException(
                nameof(secretStore)
            );
    }

    public async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length == 0)
        {
            return await ConfigureProfileAsync(
                requestedProfileName:
                    null
            );
        }

        if (args.Length == 1
            && string.Equals(
                args[0],
                "status",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return await WriteStatusAsync();
        }

        if (args.Length == 2
            && string.Equals(
                args[0],
                "profile",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return await ConfigureProfileAsync(
                args[1]
            );
        }

        if (args.Length == 3
            && string.Equals(
                args[0],
                "profile",
                StringComparison.OrdinalIgnoreCase
            )
            && string.Equals(
                args[1],
                "delete",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return await DeleteProfileAsync(
                args[2]
            );
        }

        if (args.Length == 2
            && string.Equals(
                args[0],
                "use",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return await UseProfileAsync(
                args[1]
            );
        }

        if (args.Length == 1
            && args[0] is "--help" or "-h")
        {
            PrintUsage();

            return 0;
        }

        PrintUsage();

        return 1;
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            "Configuration"
        );

        Console.WriteLine();

        Console.WriteLine(
            "  SupportToolkit configure"
        );

        Console.WriteLine(
            "  SupportToolkit configure profile <name>"
        );

        Console.WriteLine(
            "  SupportToolkit configure profile delete <name>"
        );

        Console.WriteLine(
            "  SupportToolkit configure use <name>"
        );

        Console.WriteLine(
            "  SupportToolkit configure status"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Profiles define runtime behavior, external write permission, " +
            "and reusable provider connections."
        );

        Console.WriteLine(
            "Deleting a profile does not delete shared provider " +
            "connections or stored credentials."
        );

        Console.WriteLine(
            "Authentication secrets are stored separately in the " +
            "configured secret store."
        );
    }

    private sealed record PendingSecret(
        string Key,
        string Value
    );
}