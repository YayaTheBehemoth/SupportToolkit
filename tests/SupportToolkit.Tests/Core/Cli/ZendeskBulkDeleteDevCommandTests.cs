using SupportToolkit.Cli.Commands;

namespace SupportToolkit.Tests.Cli;

public class ZendeskBulkDeleteDevCommandTests
{
    [Theory]
    [InlineData("--production", "--organization", "Example Org")]
    [InlineData("--production", "--requester", "someone@example.com")]
    [InlineData("--production", "--domain", "example.com")]
    [InlineData("--production", "--text", "invoice")]
    [InlineData("--production", "--sender", "Example Sender")]
    [InlineData("--production", "--sender", "Example Sender", "--dry-run")]
    [InlineData("--production", "--sender", "Example Sender", "--dry-run", "--include", "12,15", "--exclude", "20")]
    [InlineData("--production", "--sender", "Example Sender", "--delete")]
    [InlineData("--production", "--sender", "Example Sender", "--delete", "--include", "#12", "--receipt-dir", "somewhere")]
    [InlineData(
        "--production", "--organization", "Example Org",
        "--domain", "example.com", "--text", "invoice")]
    public void Arguments_AcceptTheDocumentedShapes(params string[] args)
    {
        Assert.True(
            ZendeskBulkDeleteDevCommand.TryParseArguments(
                args,
                out var options,
                out _));

        Assert.NotNull(options);
        Assert.True(options.Criteria.HasAny);
    }

    [Theory]
    [InlineData]
    [InlineData("--organization", "Example Org")]
    [InlineData("--production")]
    [InlineData("--production", "--organization")]
    [InlineData("--production", "--organization", " ")]
    [InlineData("--production", "--sender")]
    [InlineData("--production", "--sender", "a", "--delete", "--dry-run")]
    [InlineData("--production", "--sender", "a", "--include", "1")]
    [InlineData("--production", "--sender", "a", "--exclude", "1")]
    [InlineData("--production", "--sender", "a", "--dry-run", "--receipt-dir", "x")]
    [InlineData("--production", "--sender", "a", "--delete", "--include", "abc")]
    [InlineData("--production", "--sender", "a", "--delete", "--include", "0")]
    [InlineData("--production", "--sender", "a", "--delete", "--include", "-4")]
    [InlineData("--production", "--sender", "a", "--delete", "--include", ",")]
    [InlineData("--production", "--sender", "a", "--delete", "--include", "1,2", "--exclude", "2,3")]
    [InlineData("--production", "--sender", "a", "--delete", "--delete")]
    [InlineData("--production", "--delete")]
    [InlineData("--production", "--dry-run")]
    [InlineData("--production", "--sender", "a", "--sender", "b")]
    [InlineData("--production", "--organization", "a", "--organization", "b")]
    [InlineData("--production", "--unknown", "x")]
    [InlineData("--production", "--requester", "not-an-email")]
    [InlineData("--production", "--domain", "bad domain")]
    public void Arguments_RejectEverythingElse_IncludingAnEmptySearch(
        params string[] args)
    {
        Assert.False(
            ZendeskBulkDeleteDevCommand.TryParseArguments(
                args,
                out var options,
                out var error));

        Assert.Null(options);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void WithoutDeleteOrDryRun_ItIsAReadOnlyPreview()
    {
        ZendeskBulkDeleteDevCommand.TryParseArguments(
            ["--production", "--sender", "Example Sender"],
            out var options,
            out _);

        Assert.Equal(ZendeskBulkDeleteMode.Preview, options!.Mode);
        Assert.Null(options.ReceiptDirectory);
    }

    [Fact]
    public void DryRun_AndDelete_AreDistinctModes_WithParsedIds()
    {
        ZendeskBulkDeleteDevCommand.TryParseArguments(
            ["--production", "--sender", "a", "--dry-run"],
            out var dry,
            out _);

        Assert.Equal(ZendeskBulkDeleteMode.DryRun, dry!.Mode);

        ZendeskBulkDeleteDevCommand.TryParseArguments(
            [
                "--production", "--sender", "a", "--delete",
                "--include", "12, #15,12", "--exclude", "20",
                "--receipt-dir", "somewhere"
            ],
            out var delete,
            out _);

        Assert.Equal(ZendeskBulkDeleteMode.Delete, delete!.Mode);
        Assert.Equal([12L, 15L], delete.Include);
        Assert.Equal([20L], delete.Exclude);
        Assert.Equal("somewhere", delete.ReceiptDirectory);
    }

    [Fact]
    public void DeleteAndDryRunTogether_AreExplainedNotJustRejected()
    {
        ZendeskBulkDeleteDevCommand.TryParseArguments(
            ["--production", "--sender", "a", "--delete", "--dry-run"],
            out _,
            out var error);

        Assert.Contains("not both", error);
    }

    [Fact]
    public void AnOverlappingIncludeAndExclude_IsExplained()
    {
        ZendeskBulkDeleteDevCommand.TryParseArguments(
            [
                "--production", "--sender", "a", "--delete",
                "--include", "1,2", "--exclude", "2,3"
            ],
            out _,
            out var error);

        Assert.Contains("both --include and --exclude", error);
    }

    [Fact]
    public void AnEmptySearch_IsRefused_BecauseItWouldSelectEveryTicket()
    {
        ZendeskBulkDeleteDevCommand.TryParseArguments(
            ["--production"],
            out _,
            out var error);

        Assert.Contains("every ticket", error);
    }
}
