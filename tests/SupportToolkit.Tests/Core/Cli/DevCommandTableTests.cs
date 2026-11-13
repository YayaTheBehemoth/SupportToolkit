using SupportToolkit.Cli.Commands;
using SupportToolkit.Core.Configuration;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests;

/// <summary>
/// The developer diagnostics are found by name, and every one of them
/// rejects arguments it does not know with its own usage line, before
/// any configuration is read or any system is contacted.
/// </summary>
[Collection("ConsoleOutput")]
public class DevCommandTableTests
{
    private static readonly string[] Names =
    [
        "acronis-tenants",
        "zendesk-bulk-delete",
        "acronis-usage",
        "bc-subscriptions",
        "customer-matching",
        "monitoring-scope",
        "customer-mapping",
        "scoped-backup-review"
    ];

    [Fact]
    public void Table_HoldsExactlyTheDocumentedCommands()
    {
        Assert.Equal(
            Names.Order(),
            Table().Select(command => command.Name).Order());
    }

    [Theory]
    [InlineData("dev", "acronis-tenants")]
    [InlineData("DEV", "Acronis-Tenants")]
    public void Find_MatchesTheNameIgnoringCase(params string[] args)
    {
        Assert.Equal(
            "acronis-tenants",
            DevCommandTable.Find(Table(), args)?.Name);
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("dev", "usage-compare")]
    [InlineData("dev", "no-such-command")]
    [InlineData("acronis-tenants", "dev")]
    public void Find_LeavesEverythingElseToModuleDispatch(params string[] args)
    {
        Assert.Null(DevCommandTable.Find(Table(), args));
    }

    [Fact]
    public async Task EveryCommand_RejectsAnUnknownArgument_WithItsOwnUsageLine()
    {
        var originalError = Console.Error;

        try
        {
            foreach (var command in Table())
            {
                using var error = new StringWriter();
                Console.SetError(error);

                var exit =
                    await command.RunAsync(["dev", command.Name, "--bogus"]);

                Assert.Equal(1, exit);
                Assert.Contains(
                    $"Usage: SupportToolkit dev {command.Name}",
                    error.ToString());
            }
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private static IReadOnlyList<DevCommand> Table()
    {
        var (_, _, resolver) = Create(new SupportToolkitConfiguration());

        return DevCommandTable.Create(resolver);
    }
}
