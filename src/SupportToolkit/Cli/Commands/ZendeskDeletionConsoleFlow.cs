using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;

namespace SupportToolkit.Cli.Commands;

public enum ZendeskBulkDeleteMode
{
    /// <summary>Search and preview only.</summary>
    Preview,

    /// <summary>Show exactly what would be sent. Sends nothing.</summary>
    DryRun,

    /// <summary>Delete, after two typed confirmations.</summary>
    Delete
}

/// <summary>
/// The console side of selection, dry run and deletion: it turns a
/// preview into a selection and a frozen plan, asks for the typed
/// confirmations, and prints progress and the result. The deleting itself
/// is the executor's, which contains no console code.
///
/// Anything unclear is stated as unknown and the exit code is 2. Exit 0
/// means every selected ticket was reported deleted (or, for a dry run or
/// an empty selection, that nothing needed doing).
/// </summary>
public sealed class ZendeskDeletionConsoleFlow
{
    private readonly ZendeskDeletionExecutor? _executor;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TimeProvider _timeProvider;

    public ZendeskDeletionConsoleFlow(
        ZendeskDeletionExecutor? executor,
        TextReader input,
        TextWriter output,
        TimeProvider? timeProvider = null)
    {
        _executor = executor;

        _input =
            input ?? throw new ArgumentNullException(nameof(input));

        _output =
            output ?? throw new ArgumentNullException(nameof(output));

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<int> RunAsync(
        DeletionPreviewResult preview,
        ZendeskBulkDeleteOptions options,
        string instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(options);

        var selection = new DeletionSelection(preview.Preview);

        try
        {
            selection.Select(options.Include ?? []);
            selection.Deselect(options.Exclude ?? []);
        }
        catch (ArgumentException exception)
        {
            _output.WriteLine();
            _output.WriteLine("Selection error: " + exception.Message);
            return 1;
        }

        _output.WriteLine();
        _output.WriteLine("SELECTION");
        _output.WriteLine("---------");
        _output.WriteLine(
            $"Selected {selection.Count} of {preview.Preview.Total} " +
            "ticket(s).");

        if (selection.Count == 0)
        {
            _output.WriteLine(
                "Nothing is selected, so nothing will be deleted. " +
                "Closed tickets are selected by default; use --include " +
                "<ids> to add others.");

            return 0;
        }

        if (!preview.Preview.IsSearchComplete)
        {
            _output.WriteLine(
                "Not deleting: the search was incomplete, so this is " +
                "not the full set of matching tickets. Narrow the search.");

            return 2;
        }

        DeletionPlan plan;

        try
        {
            plan =
                DeletionPlan.Create(
                    preview,
                    selection,
                    instance,
                    _timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            _output.WriteLine("Cannot plan the deletion: " + exception.Message);
            return 1;
        }

        WritePlan(plan);

        if (options.Mode == ZendeskBulkDeleteMode.DryRun)
        {
            WriteDryRun(plan);
            return 0;
        }

        return await DeleteAsync(plan, cancellationToken);
    }

    private async Task<int> DeleteAsync(
        DeletionPlan plan,
        CancellationToken cancellationToken)
    {
        if (_executor is null)
        {
            _output.WriteLine("Deletion is not enabled for this session.");
            return 1;
        }

        _output.WriteLine();
        _output.WriteLine(
            $"This will delete {plan.Tickets.Count} ticket(s) from " +
            $"{plan.Instance}.zendesk.com. Zendesk keeps deleted tickets " +
            "for 30 days.");

        _output.Write(
            $"Confirmation 1 of {(plan.NonClosed.Count > 0 ? 2 : 1)}: " +
            $"type the number of tickets to delete ({plan.Tickets.Count}) " +
            "to continue, or anything else to cancel: ");

        if (!await ReadNumberAsync(plan.Tickets.Count, cancellationToken))
        {
            return Cancelled();
        }

        int? nonClosedCount = null;

        if (plan.NonClosed.Count > 0)
        {
            _output.WriteLine();
            _output.WriteLine(
                $"{plan.NonClosed.Count} of these ticket(s) are NOT " +
                "closed (or have an unknown closing time):");

            foreach (var ticket in plan.NonClosed)
            {
                _output.WriteLine(
                    $"  #{ticket.Id}  {ticket.Status}");
            }

            _output.Write(
                "Confirmation 2 of 2: type the number of tickets that " +
                $"are NOT closed ({plan.NonClosed.Count}) to delete " +
                "them as well, or anything else to cancel: ");

            if (!await ReadNumberAsync(
                    plan.NonClosed.Count,
                    cancellationToken))
            {
                return Cancelled();
            }

            nonClosedCount = plan.NonClosed.Count;
        }

        _output.WriteLine();
        _output.WriteLine("Deleting...");

        DeletionRun run;

        try
        {
            run =
                await _executor.ExecuteAsync(
                    plan,
                    new DeletionConfirmation(
                        plan.Tickets.Count,
                        nonClosedCount),
                    WriteProgress,
                    cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            _output.WriteLine();
            _output.WriteLine(exception.Message);
            return 2;
        }

        WriteResult(run);

        return run.Outcome.IsComplete && run.ReceiptWarning is null
            ? 0
            : 2;
    }

    private void WritePlan(
        DeletionPlan plan)
    {
        _output.WriteLine();
        _output.WriteLine("DELETION PLAN");
        _output.WriteLine("-------------");
        _output.WriteLine(
            $"Instance:  {plan.Instance}.zendesk.com");
        _output.WriteLine(
            $"Tickets:   {plan.Tickets.Count} in " +
            $"{plan.Batches.Count} request(s) of up to " +
            $"{DeletionPlan.BatchSize}");
        _output.WriteLine(
            $"Not closed: {plan.NonClosed.Count}");
        _output.WriteLine();
        _output.WriteLine("Exact ticket IDs (nothing else will be sent):");

        foreach (var line in plan.Tickets
                     .Select(ticket => "#" + ticket.Id)
                     .Chunk(10))
        {
            _output.WriteLine("  " + string.Join("  ", line));
        }
    }

    private void WriteDryRun(
        DeletionPlan plan)
    {
        _output.WriteLine();
        _output.WriteLine("DRY RUN: this is what would be sent. Nothing is.");

        var number = 0;

        foreach (var batch in plan.Batches)
        {
            number++;

            _output.WriteLine(
                $"  {number}. DELETE /api/v2/tickets/destroy_many.json" +
                $"?ids=<{batch.Count} ticket ID(s): " +
                $"{batch[0]}..{batch[^1]}>");
        }

        _output.WriteLine(
            "Each request starts a job that is followed to its end.");
    }

    private void WriteProgress(
        DeletionProgress progress)
    {
        var text =
            progress.Phase switch
            {
                DeletionProgressPhase.StartingBatch =>
                    $"Request {progress.Batch} of {progress.BatchCount}: " +
                    $"{progress.TicketsInBatch} ticket(s)...",

                DeletionProgressPhase.WaitingForJob =>
                    $"  job {progress.JobStatus} " +
                    $"({progress.JobProgress?.ToString() ?? "?"}/" +
                    $"{progress.JobTotal?.ToString() ?? "?"})",

                DeletionProgressPhase.BatchFinished =>
                    $"Request {progress.Batch} finished.",

                DeletionProgressPhase.Stopped =>
                    $"Stopped at request {progress.Batch} of " +
                    $"{progress.BatchCount}.",

                _ => string.Empty
            };

        if (text.Length > 0)
        {
            _output.WriteLine(text);
        }
    }

    private void WriteResult(
        DeletionRun run)
    {
        var outcome = run.Outcome;

        _output.WriteLine();
        _output.WriteLine("RESULT");
        _output.WriteLine("------");
        _output.WriteLine(
            $"Deleted:       {outcome.Count(TicketDeletionState.Deleted)}");
        _output.WriteLine(
            $"Failed:        {outcome.Count(TicketDeletionState.Failed)}");
        _output.WriteLine(
            $"Unknown:       {outcome.Count(TicketDeletionState.Unknown)}");
        _output.WriteLine(
            $"Not attempted: {outcome.Count(TicketDeletionState.NotAttempted)}");

        var problems =
            outcome.Results
                .Where(
                    result => result.State
                        is TicketDeletionState.Failed
                        or TicketDeletionState.Unknown)
                .ToArray();

        if (problems.Length > 0)
        {
            _output.WriteLine();
            _output.WriteLine(
                "Not confirmed deleted (an Unknown ticket may or may " +
                "not have been deleted; check Zendesk's deleted tickets):");

            foreach (var group in problems
                         .GroupBy(
                             result => (result.State, result.Detail))
                         .OrderBy(group => group.Key.State))
            {
                _output.WriteLine(
                    $"  {group.Key.State}: " +
                    string.Join(", ", group.Select(r => "#" + r.Id)) +
                    (string.IsNullOrWhiteSpace(group.Key.Detail)
                        ? string.Empty
                        : " - " + group.Key.Detail));
            }
        }

        _output.WriteLine();
        _output.WriteLine($"Receipt: {run.ReceiptPath}");

        if (run.ReceiptWarning is not null)
        {
            _output.WriteLine("WARNING: " + run.ReceiptWarning);
        }

        _output.WriteLine(
            "Keep the receipt: it lists the ticket IDs needed to " +
            "restore them. It is not shared or committed.");
    }

    private async Task<bool> ReadNumberAsync(
        int expected,
        CancellationToken cancellationToken)
    {
        var line = await _input.ReadLineAsync(cancellationToken);

        return int.TryParse(line?.Trim(), out var typed)
               && typed == expected;
    }

    private int Cancelled()
    {
        _output.WriteLine();
        _output.WriteLine("Cancelled. Nothing was deleted.");

        return 1;
    }
}
