
using SupportToolkit.Core.Logging;

namespace SupportToolkit.Tests.Core.Logging;

public class OperationalLoggerTests
{
    private const string SyntheticUuid =
        "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    [Fact]
    public void Info_DebugDisabled_ProducesNoOutput()
    {
        using var output = new StringWriter();

        var logger =
            new OperationalLogger(
                debugEnabled: false,
                output: output
            );

        logger.Info(
            $"GET /api/2/tenants/{SyntheticUuid}"
        );

        logger.Debug(
            "Diagnostic details"
        );

        Assert.Equal(
            string.Empty,
            output.ToString()
        );
    }

    [Fact]
    public void Info_DebugEnabled_RedactsUuidInRequestPath()
    {
        using var output = new StringWriter();

        var logger =
            new OperationalLogger(
                debugEnabled: true,
                output: output
            );

        logger.Info(
            $"GET /bc/api/resource_manager/v1/o365/groups/" +
            $"{SyntheticUuid}/resources -> 200 (123 ms)"
        );

        var text =
            output.ToString();

        Assert.Contains(
            "[INFO]",
            text
        );

        Assert.Contains(
            "/bc/api/resource_manager/v1/o365/groups/" +
            "{uuid}/resources -> 200 (123 ms)",
            text
        );

        Assert.DoesNotContain(
            SyntheticUuid,
            text,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Warning_DebugDisabled_RemainsVisibleAndRedacted()
    {
        using var output = new StringWriter();

        var logger =
            new OperationalLogger(
                debugEnabled: false,
                output: output
            );

        logger.Warning(
            $"Tenant {SyntheticUuid} could not be reviewed."
        );

        var text =
            output.ToString();

        Assert.Contains(
            "[WARN]",
            text
        );

        Assert.Contains(
            "Tenant {uuid} could not be reviewed.",
            text
        );

        Assert.DoesNotContain(
            SyntheticUuid,
            text
        );
    }

    [Fact]
    public void Debug_MultipleUuids_AreAllRedacted()
    {
        using var output = new StringWriter();

        var logger =
            new OperationalLogger(
                debugEnabled: true,
                output: output
            );

        var secondUuid =
            "11111111-2222-3333-4444-555555555555";

        logger.Debug(
            $"Group {SyntheticUuid} belongs to {secondUuid}."
        );

        var text =
            output.ToString();

        Assert.Contains(
            "Group {uuid} belongs to {uuid}.",
            text
        );

        Assert.DoesNotContain(
            SyntheticUuid,
            text
        );

        Assert.DoesNotContain(
            secondUuid,
            text
        );
    }

    [Fact]
    public void Warning_Newlines_DoNotCreateAdditionalLogEntries()
    {
        using var output = new StringWriter();

        var logger =
            new OperationalLogger(
                output: output
            );

        logger.Warning(
            "Request failed.\r\nFake additional log line."
        );

        var lines =
            output.ToString().Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries
            );

        Assert.Single(lines);

        Assert.Contains(
            "Request failed.  Fake additional log line.",
            lines[0]
        );

        Assert.Contains(
            "[WARN]",
            lines[0]
        );
    }

    [Fact]
    public void FromEnvironment_EnablesDebugOnlyForTrue()
    {
        var enabled =
            OperationalLogger.FromEnvironment(
                variable =>
                    variable == "SUPPORTTOOLKIT_DEBUG"
                        ? "TRUE"
                        : null
            );

        var disabled =
            OperationalLogger.FromEnvironment(
                _ => "false"
            );

        Assert.True(enabled.DebugEnabled);
        Assert.False(disabled.DebugEnabled);
    }
}
