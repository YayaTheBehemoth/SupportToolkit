using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Production;
using SupportToolkit.Modules.ZendeskBulkDelete.Reporting;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Providers.Zendesk.Deletion.Search;

namespace SupportToolkit.Cli.Commands;

public sealed record ZendeskBulkDeleteOptions(
    TicketSearchCriteria Criteria,
    ZendeskBulkDeleteMode Mode = ZendeskBulkDeleteMode.Preview,
    IReadOnlyList<long>? Include = null,
    IReadOnlyList<long>? Exclude = null,
    string? ReceiptDirectory = null);

/// <summary>
/// Developer path for the Zendesk bulk delete: sign in, search, preview,
/// and, only when asked, select and delete.
///
/// Without --delete or --dry-run it only reads. --dry-run shows exactly
/// what would be sent and sends nothing. --delete deletes the selected
/// tickets after two typed confirmations and keeps a receipt.
///
/// It uses the profile's Zendesk deletion connection, never the ticketing
/// connection. Interactive only: the sign-in opens a browser, and
/// confirmations are typed, so it refuses to run with redirected input or
/// output. A search without any criterion is refused, since it would
/// select every ticket.
///
/// The CLI dispatcher must require an explicit --production argument.
/// </summary>
public sealed class ZendeskBulkDeleteDevCommand
{
    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    public ZendeskBulkDeleteDevCommand(
        SupportToolkitConfigurationResolver configurationResolver)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver));
    }

    public static bool TryParseArguments(
        IReadOnlyList<string> args,
        out ZendeskBulkDeleteOptions? options,
        out string error)
    {
        options = null;
        error =
            "Usage: SupportToolkit dev zendesk-bulk-delete --production " +
            "[--organization <name>] [--sender <name>] " +
            "[--requester <email>] [--domain <domain>] " +
            "[--text <search text>] " +
            "[--dry-run | --delete [--include <ids>] [--exclude <ids>] " +
            "[--receipt-dir <path>]] " +
            "(at least one search option is required)";

        if (args.Count == 0
            || !string.Equals(
                args[0],
                "--production",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? organization = null;
        string? requester = null;
        string? domain = null;
        string? text = null;
        string? sender = null;
        string? include = null;
        string? exclude = null;
        string? receiptDirectory = null;
        var delete = false;
        var dryRun = false;

        for (var index = 1; index < args.Count; index++)
        {
            var argument = args[index];

            if (Is(argument, "--delete") && !delete)
            {
                delete = true;
                continue;
            }

            if (Is(argument, "--dry-run") && !dryRun)
            {
                dryRun = true;
                continue;
            }

            if (index + 1 >= args.Count
                || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return false;
            }

            var value = args[index + 1];

            if (Is(argument, "--organization") && organization is null)
            {
                organization = value;
            }
            else if (Is(argument, "--requester") && requester is null)
            {
                requester = value;
            }
            else if (Is(argument, "--domain") && domain is null)
            {
                domain = value;
            }
            else if (Is(argument, "--text") && text is null)
            {
                text = value;
            }
            else if (Is(argument, "--sender") && sender is null)
            {
                sender = value;
            }
            else if (Is(argument, "--include") && include is null)
            {
                include = value;
            }
            else if (Is(argument, "--exclude") && exclude is null)
            {
                exclude = value;
            }
            else if (Is(argument, "--receipt-dir") && receiptDirectory is null)
            {
                receiptDirectory = value;
            }
            else
            {
                return false;
            }

            index++;
        }

        if (delete && dryRun)
        {
            error = "Use either --dry-run or --delete, not both.";
            return false;
        }

        if ((include is not null || exclude is not null)
            && !delete
            && !dryRun)
        {
            error =
                "--include and --exclude need --dry-run or --delete.";
            return false;
        }

        if (receiptDirectory is not null && !delete)
        {
            error = "--receipt-dir needs --delete.";
            return false;
        }

        if (!TryParseIds(include, "--include", out var includeIds, out error)
            || !TryParseIds(exclude, "--exclude", out var excludeIds, out error))
        {
            return false;
        }

        if (includeIds.Intersect(excludeIds).Any())
        {
            error =
                "A ticket cannot be in both --include and --exclude.";
            return false;
        }

        var criteria =
            new TicketSearchCriteria(
                organization,
                requester,
                domain,
                text,
                sender);

        if (!criteria.HasAny)
        {
            error =
                "At least one search option is required: an empty " +
                "search would select every ticket.";
            return false;
        }

        try
        {
            ZendeskSearchQuery.Build(ZendeskTicketDirectory.ToZendesk(criteria));
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        options =
            new ZendeskBulkDeleteOptions(
                criteria,
                delete
                    ? ZendeskBulkDeleteMode.Delete
                    : dryRun
                        ? ZendeskBulkDeleteMode.DryRun
                        : ZendeskBulkDeleteMode.Preview,
                includeIds,
                excludeIds,
                receiptDirectory);

        error = string.Empty;

        return true;
    }

    public async Task<int> RunAsync(
        TextWriter output,
        ZendeskBulkDeleteOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            await Console.Error.WriteLineAsync(
                "The Zendesk bulk delete is interactive only: it opens a " +
                "browser for sign-in and asks for typed confirmation, so " +
                "it cannot run unattended or with redirected input or " +
                "output.");

            return 1;
        }

        var logger = OperationalLogger.FromEnvironment();

        using var session =
            await ZendeskBulkDeleteComposition.OpenSessionAsync(
                _configurationResolver,
                logger.DebugEnabled ? logger : null,
                options.Mode == ZendeskBulkDeleteMode.Delete,
                options.ReceiptDirectory,
                cancellationToken);

        output.WriteLine(
            $"Signing in to {session.Options.Subdomain}.zendesk.com " +
            (options.Mode == ZendeskBulkDeleteMode.Delete
                ? "(browser; this command can DELETE tickets)..."
                : "(browser, read-only preview, nothing is deleted)..."));

        var operatorUser =
            await session.Workflow.SignInAsync(cancellationToken);

        output.WriteLine("Signed in. Searching...");

        var result =
            await session.Workflow.PreviewAsync(
                operatorUser,
                options.Criteria,
                cancellationToken);

        ConsoleDeletionPreviewReporter.Write(output, result);

        if (options.Mode == ZendeskBulkDeleteMode.Preview)
        {
            // An incomplete search is never a clean result.
            return result.Preview.IsSearchComplete ? 0 : 2;
        }

        return await new ZendeskDeletionConsoleFlow(
                session.Executor,
                Console.In,
                output)
            .RunAsync(
                result,
                options,
                session.Options.Subdomain,
                cancellationToken);
    }

    private static bool TryParseIds(
        string? value,
        string name,
        out IReadOnlyList<long> ids,
        out string error)
    {
        ids = [];
        error = string.Empty;

        if (value is null)
        {
            return true;
        }

        var parsed = new List<long>();

        foreach (var part in value.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            var digits = part.TrimStart('#');

            if (!digits.All(char.IsAsciiDigit)
                || !long.TryParse(digits, out var id)
                || id <= 0)
            {
                error =
                    $"{name} takes comma-separated ticket IDs, such as " +
                    "12,15,20.";
                return false;
            }

            parsed.Add(id);
        }

        if (parsed.Count == 0)
        {
            error = $"{name} needs at least one ticket ID.";
            return false;
        }

        ids = parsed.Distinct().ToArray();

        return true;
    }

    private static bool Is(
        string argument,
        string name) =>
        string.Equals(
            argument,
            name,
            StringComparison.OrdinalIgnoreCase);
}
