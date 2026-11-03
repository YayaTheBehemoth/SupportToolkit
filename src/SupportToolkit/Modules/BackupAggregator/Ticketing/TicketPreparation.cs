using System.Diagnostics.CodeAnalysis;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Modules.BackupAggregator.Ticketing;

/// <summary>
/// Why a ticket cannot be prepared yet. A user interface chooses its own
/// wording for each kind; the reasons are the facts behind it.
/// </summary>
public enum TicketNotReadyKind
{
    /// <summary>No review has been run, so there is nothing to report.</summary>
    NoReview,

    /// <summary>
    /// The review is not complete (an unresolved scope, a failed tenant
    /// review, ...). A ticket would claim coverage it does not have.
    /// </summary>
    ReviewIncomplete,

    /// <summary>
    /// The Zendesk destination or credentials are missing or invalid.
    /// </summary>
    DestinationUnavailable
}

public sealed record TicketNotReady(
    TicketNotReadyKind Kind,
    IReadOnlyList<string> Reasons);

/// <summary>
/// The outcome of preparing a ticket: either ready, with its value, or
/// not ready, with typed reasons. Expected conditions are results, not
/// exceptions, so every user interface can present them the same way.
/// </summary>
public sealed class TicketPreparation<T>
    where T : class
{
    private TicketPreparation(
        T? value,
        TicketNotReady? notReady)
    {
        Value = value;
        NotReady = notReady;
    }

    public T? Value { get; }

    public TicketNotReady? NotReady { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(NotReady))]
    public bool IsReady => NotReady is null;

    public static TicketPreparation<T> Ready(
        T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new TicketPreparation<T>(value, null);
    }

    public static TicketPreparation<T> Blocked(
        TicketNotReadyKind kind,
        params string[] reasons) =>
        Blocked(new TicketNotReady(kind, reasons));

    public static TicketPreparation<T> Blocked(
        TicketNotReady notReady)
    {
        ArgumentNullException.ThrowIfNull(notReady);

        return new TicketPreparation<T>(null, notReady);
    }
}

/// <summary>
/// Everything an operator confirms before a ticket is submitted: the exact
/// draft, the Zendesk destination it will go to, and what the review
/// found. Submission sends this draft to this destination, nothing else;
/// if the configured destination has changed since, it fails closed.
/// </summary>
public sealed record TicketSubmissionPlan(
    TicketDraft Draft,
    ZendeskTicketDestinationPreview Destination,
    ReviewSummary Review);
