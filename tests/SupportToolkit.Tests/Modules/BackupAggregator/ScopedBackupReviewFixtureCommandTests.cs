
using System.Globalization;
using SupportToolkit.Cli.Commands;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class ScopedBackupReviewFixtureCommandTests
{
    [Fact]
    public async Task RunAsync_CollectsSelectedInventoriesAndRetainsScopeWarnings()
    {
        using var writer =
            new StringWriter(
                CultureInfo.InvariantCulture
            );

        var command =
            new ScopedBackupReviewFixtureCommand();

        var exitCode =
            await command.RunAsync(
                writer,
                new DateOnly(2026, 10, 15)
            );

        var output =
            writer.ToString();

        Assert.Equal(0, exitCode);

        Assert.Contains(
            "External requests: 0",
            output
        );

        Assert.Contains(
            "Inventory candidates: 3",
            output
        );

        Assert.Contains(
            "Needs scope review: 1",
            output
        );

        Assert.Contains(
            "Unclassified: 1",
            output
        );

        Assert.Contains(
            "Tenants attempted: 3",
            output
        );

        Assert.Contains(
            "Tenants reviewed: 3",
            output
        );

        Assert.Contains(
            "Resources checked: 3",
            output
        );

        Assert.Contains(
            "Healthy resources: 3",
            output
        );

        Assert.Contains(
            "Selected inventory complete: YES",
            output
        );

        Assert.Contains(
            "Monitoring scope complete: NO",
            output
        );

        Assert.Contains(
            "Overall complete: NO",
            output
        );

        Assert.Contains(
            "Contoso Workshop | NeedsReview",
            output
        );

        Assert.Contains(
            "Alpine Consulting | Unclassified",
            output
        );
    }

    [Fact]
    public async Task RunAsync_NullWriter_Throws()
    {
        var command =
            new ScopedBackupReviewFixtureCommand();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => command.RunAsync(
                null!,
                new DateOnly(2026, 10, 15)
            )
        );
    }
}
