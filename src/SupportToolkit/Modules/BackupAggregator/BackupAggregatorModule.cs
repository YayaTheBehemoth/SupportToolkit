using SupportToolkit.Core.Modules;

namespace SupportToolkit.Modules.BackupAggregator;

public sealed class BackupAggregatorModule
    : ISupportToolkitModule
{
    public string Command =>
        "backup-aggregator";

    public string Description =>
        "Aggregate backup health results into a consolidated operational report.";

    public Task<int> RunAsync(
        string[] args)
    {
        if (args.Length > 0)
        {
            throw new InvalidOperationException(
                $"The '{Command}' module does not accept arguments yet."
            );
        }

        Console.WriteLine(
            "BackupAggregator module is ready."
        );

        return Task.FromResult(
            0
        );
    }
}