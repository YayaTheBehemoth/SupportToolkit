namespace SupportToolkit.Modules.ZendeskBulkDelete.Models;

/// <summary>
/// The record of one deletion run, kept as a JSON file outside the
/// repository.
///
/// It lists every ticket ID and what happened to it, so deleted tickets
/// can be restored by ID later (Zendesk keeps deleted tickets for 30 days,
/// and deleted archived tickets can only be restored through the API by
/// ID). It never contains ticket subjects or content.
///
/// A first receipt is written before anything is deleted (state
/// "started"), and rewritten when the run ends ("finished").
/// </summary>
public sealed record DeletionReceipt(
    int SchemaVersion,
    string State,
    string RunId,
    string Instance,
    ReceiptOperator Operator,
    string Query,
    DateTimeOffset PlannedAt,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    ReceiptConfirmation Confirmation,
    ReceiptSummary Summary,
    IReadOnlyList<ReceiptTicket> Tickets)
{
    public const int CurrentSchemaVersion = 1;

    public const string Started = "started";
    public const string Finished = "finished";

    public static DeletionReceipt From(
        DeletionPlan plan,
        DeletionConfirmation confirmation,
        DateTimeOffset startedAt,
        DateTimeOffset? finishedAt,
        IReadOnlyCollection<TicketDeletionResult> results)
    {
        var byId = results.ToDictionary(result => result.Id);

        int Count(TicketDeletionState state) =>
            results.Count(result => result.State == state);

        return new DeletionReceipt(
            CurrentSchemaVersion,
            finishedAt is null ? Started : Finished,
            plan.Id.ToString("D"),
            plan.Instance,
            new ReceiptOperator(
                plan.Operator.Id,
                plan.Operator.Name,
                plan.Operator.Email,
                plan.Operator.Role),
            plan.Query,
            plan.CreatedAt,
            startedAt,
            finishedAt,
            new ReceiptConfirmation(
                confirmation.ConfirmedCount,
                confirmation.ConfirmedNonClosedCount),
            new ReceiptSummary(
                plan.Tickets.Count,
                Count(TicketDeletionState.Deleted),
                Count(TicketDeletionState.Failed),
                Count(TicketDeletionState.Unknown),
                Count(TicketDeletionState.NotAttempted)),
            plan.Tickets
                .Select(
                    ticket =>
                    {
                        var result = byId[ticket.Id];

                        return new ReceiptTicket(
                            ticket.Id,
                            ticket.Status,
                            ticket.Flag?.ToString(),
                            result.State.ToString(),
                            result.Detail,
                            result.JobId);
                    })
                .ToArray());
    }
}

public sealed record ReceiptOperator(
    long Id,
    string? Name,
    string? Email,
    string? Role);

public sealed record ReceiptConfirmation(
    int Count,
    int? NonClosedCount);

public sealed record ReceiptSummary(
    int Planned,
    int Deleted,
    int Failed,
    int Unknown,
    int NotAttempted);

public sealed record ReceiptTicket(
    long Id,
    string StatusAtPlan,
    string? FlagAtPlan,
    string Outcome,
    string? Detail,
    string? JobId);
