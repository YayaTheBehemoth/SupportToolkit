using System.Globalization;

namespace SupportToolkit.Modules.BulkDelete.Models;

/// <summary>
/// What deleting tickets in one ticketing system is like, as that system's
/// production adapter declares it. The module, the console and the web page
/// read every system-specific limit and caveat from here, so nothing above
/// the adapter knows which system it is deleting from.
///
/// A rule a system does not have is null: no waiting time after closing,
/// no archive, no way to restore. Each operator-facing sentence is built
/// from these values, so it states what this system actually does.
/// </summary>
public sealed record TicketSystemRules
{
    /// <summary>The system as operators know it ("Zendesk").</summary>
    public required string SystemName { get; init; }

    /// <summary>
    /// The start of a receipt's file name ("zendesk"), so receipts of
    /// different systems are told apart.
    /// </summary>
    public required string ReceiptPrefix { get; init; }

    /// <summary>
    /// The instance as operators know it, from the instance name the plan
    /// records ("example" becomes "example.zendesk.com").
    /// </summary>
    public required Func<string, string> DisplayInstance { get; init; }

    /// <summary>The most ticket IDs one deletion request may carry.</summary>
    public required int BatchSize { get; init; }

    /// <summary>
    /// How long after closing a ticket cannot be deleted, or null when it
    /// can be deleted straight away.
    /// </summary>
    public TimeSpan? DeleteBlockedAfterClosing { get; init; }

    /// <summary>
    /// How long after closing a ticket is archived, or null when the
    /// system does not archive. A deleted archived ticket is harder to
    /// restore.
    /// </summary>
    public TimeSpan? ArchiveAge { get; init; }

    /// <summary>
    /// How many days a deleted ticket can be restored from, or null when
    /// deletion is immediate and permanent.
    /// </summary>
    public int? RecoverableDays { get; init; }

    /// <summary>
    /// True when the operator signs in in a browser; false when the saved
    /// connection signs in without the operator.
    /// </summary>
    public required bool SignsInWithBrowser { get; init; }

    /// <summary>
    /// True when a deletion request starts a job that is followed to its
    /// end; false when the answer to the request is the outcome.
    /// </summary>
    public required bool RunsJobs { get; init; }

    /// <summary>
    /// The request one batch is sent as, for the dry run: the number of
    /// IDs, the first ID and the last ID.
    /// </summary>
    public required Func<int, long, long, string> DescribeRequest { get; init; }

    /// <summary>
    /// What the system's search may get wrong about an email domain, or
    /// null when nothing needs saying.
    /// </summary>
    public string? DomainSearchNote { get; init; }

    public bool CanRestore => RecoverableDays is not null;

    /// <summary>"24 hours", for sentences about the waiting time.</summary>
    public string BlockedWindowText =>
        DeleteBlockedAfterClosing is { } window
            ? Duration(window)
            : string.Empty;

    /// <summary>
    /// Where the operator checks an unclear outcome, after "check":
    /// "Zendesk's deleted tickets".
    /// </summary>
    public string CheckPlace =>
        CanRestore
            ? $"{SystemName}'s deleted tickets"
            : $"the tickets in {SystemName}";

    /// <summary>
    /// The sentence that ends an unclear outcome:
    /// "Check the deleted tickets in Zendesk."
    /// </summary>
    public string CheckAdvice =>
        CanRestore
            ? $"Check the deleted tickets in {SystemName}."
            : $"Check the tickets in {SystemName}.";

    /// <summary>
    /// What happens to a deleted ticket, for the warning before deleting.
    /// </summary>
    public string RecoveryNote
    {
        get
        {
            if (!CanRestore)
            {
                return $"{SystemName} deletes tickets immediately and " +
                       "permanently: there is no trash, and a deleted ticket " +
                       "cannot be restored.";
            }

            var days = RecoverableDays!.Value.ToString(CultureInfo.InvariantCulture);

            return ArchiveAge is null
                ? $"{SystemName} keeps deleted tickets for {days} days."
                : $"{SystemName} keeps deleted tickets for {days} days, except " +
                  $"old closed tickets that {SystemName} has archived: those " +
                  $"cannot be restored from {SystemName}'s deleted tickets at all.";
        }
    }

    /// <summary>The short form, for the console: "… for 30 days."</summary>
    public string ShortRecoveryNote =>
        CanRestore
            ? $"{SystemName} keeps deleted tickets for " +
              $"{RecoverableDays!.Value.ToString(CultureInfo.InvariantCulture)} days."
            : $"{SystemName} deletes them immediately and permanently; they " +
              "cannot be restored.";

    private static string Duration(TimeSpan value)
    {
        // Hours up to two days ("24 hours", as operators know the rule).
        if (value.TotalHours > 48 && value.TotalDays == Math.Floor(value.TotalDays))
        {
            return $"{(int)value.TotalDays} days";
        }

        var hours = (int)Math.Ceiling(value.TotalHours);

        return hours == 1 ? "1 hour" : $"{hours} hours";
    }
}
