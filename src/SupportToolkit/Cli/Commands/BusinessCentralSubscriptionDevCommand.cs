using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Providers.BusinessCentral;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Developer-only, read-only Business Central contract check.
///
/// Uses the active profile's Business Central connection and prompts
/// for an interactive sign-in. Prints aggregate counts only, never
/// row-level values, so the output can be shared for review.
///
/// The CLI dispatcher must require an explicit --production argument.
/// </summary>
public sealed class BusinessCentralSubscriptionDevCommand
{
    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    public BusinessCentralSubscriptionDevCommand(
        SupportToolkitConfigurationResolver configurationResolver)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver));
    }

    public async Task<int> RunAsync(
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        var bundle =
            await BusinessCentralProviderFactory.CreateAsync(
                _configurationResolver,
                cancellationToken);

        // Optional: diagnostics still run without an item number.
        string? itemNumber = null;

        try
        {
            itemNumber =
                await _configurationResolver
                    .GetMonitoringItemNumberAsync(
                        BackupAggregatorModule.ModuleCommand,
                        cancellationToken);
        }
        catch (InvalidOperationException)
        {
        }

        output.WriteLine("Signing in and reading Business Central (read-only)...");

        var data =
            await bundle.Provider.GetSubscriptionsAsync(
                cancellationToken);

        output.WriteLine();

        BusinessCentralSubscriptionDiagnostics.Write(
            output,
            data,
            itemNumber,
            bundle.Options.Environment,
            bundle.Options.CompanyName);

        return 0;
    }
}
