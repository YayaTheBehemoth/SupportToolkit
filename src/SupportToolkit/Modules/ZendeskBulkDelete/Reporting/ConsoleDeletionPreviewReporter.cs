using System.Globalization;
using SupportToolkit.Modules.ZendeskBulkDelete.Models;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Reporting;

/// <summary>
/// Prints the deletion preview. Contains customer-related names and
/// ticket subjects, so it is for the operator's own console only.
/// </summary>
public static class ConsoleDeletionPreviewReporter
{
    private const int GroupLimit = 20;
    private const int SubjectWidth = 50;

    public static void Write(
        TextWriter output,
        DeletionPreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        var preview = result.Preview;

        output.WriteLine();
        output.WriteLine("ZENDESK BULK DELETE PREVIEW (nothing is deleted)");
        output.WriteLine("-------------------------------------------------");
        output.WriteLine(
            $"Signed in as: {result.Operator.Name ?? "(no name)"} " +
            $"<{result.Operator.Email ?? "no email"}> " +
            $"(user {result.Operator.Id}, {result.Operator.Role ?? "role unknown"})");
        output.WriteLine($"Search:       {result.Query}");
        output.WriteLine();

        if (!preview.IsSearchComplete)
        {
            output.WriteLine(
                "*** INCOMPLETE SEARCH: this list is NOT the full set " +
                "of matching tickets. ***");
            output.WriteLine($"    {preview.SearchIncompleteReason}");
            output.WriteLine(
                "    Do not treat it as complete. Narrow the search.");
            output.WriteLine();
        }

        if (preview.DuplicateCount > 0)
        {
            output.WriteLine(
                $"Duplicate results removed: {preview.DuplicateCount}");
        }

        if (preview.DirectoryNote is not null)
        {
            output.WriteLine($"NOTE: {preview.DirectoryNote}");
        }

        output.WriteLine($"Tickets found:        {preview.Total}");
        output.WriteLine(
            $"Selected by default:  {preview.SelectedCount} (closed)");
        output.WriteLine(
            $"Exceptions, unselected: {preview.Exceptions.Count}");

        WriteGroups(output, "By status", preview.StatusCounts);
        WriteGroups(output, "By organization", preview.ByOrganization);
        WriteGroups(output, "By requester domain", preview.ByRequesterDomain);

        output.WriteLine();
        output.WriteLine("Exceptions (not selected)");

        if (preview.Exceptions.Count == 0)
        {
            output.WriteLine("  None.");
        }

        foreach (var ticket in preview.Exceptions)
        {
            output.WriteLine(
                $"  #{ticket.Id}  {ticket.Status,-8} {Describe(ticket.Flag)}");
        }

        output.WriteLine();
        output.WriteLine("Tickets  ([x] selected, [ ] not selected)");

        foreach (var ticket in preview.Tickets)
        {
            output.WriteLine(
                $"  [{(ticket.Selected ? "x" : " ")}] #{ticket.Id,-9} " +
                $"{ticket.Status,-8} " +
                $"{Date(ticket.UpdatedAt),-16} " +
                $"{ticket.Organization} / {ticket.RequesterDomain}  " +
                Truncate(ticket.Subject));
        }

        output.WriteLine();
        output.WriteLine(
            "Closed time is approximated by the last update. This " +
            "preview only reads; it deletes nothing.");
    }

    private static void WriteGroups(
        TextWriter output,
        string title,
        IReadOnlyList<PreviewGroup> groups)
    {
        output.WriteLine();
        output.WriteLine(title);

        foreach (var group in groups.Take(GroupLimit))
        {
            output.WriteLine($"  {group.Count,5}  {group.Name}");
        }

        if (groups.Count > GroupLimit)
        {
            output.WriteLine(
                $"        +{groups.Count - GroupLimit} more");
        }
    }

    private static string Describe(
        DeletionPreviewFlag? flag) =>
        flag switch
        {
            DeletionPreviewFlag.NotClosed =>
                "not closed",
            DeletionPreviewFlag.ClosedWithin24Hours =>
                "closed less than 24 hours ago (cannot be deleted yet)",
            DeletionPreviewFlag.ClosedTimeUnknown =>
                "closed, time unknown (treated as too recent)",
            _ => string.Empty
        };

    private static string Date(
        DateTimeOffset? value) =>
        value is null
            ? "unknown"
            : value.Value.UtcDateTime.ToString(
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture);

    private static string Truncate(
        string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return string.Empty;
        }

        var clean =
            new string(
                subject.Where(character => !char.IsControl(character))
                    .ToArray());

        return clean.Length <= SubjectWidth
            ? clean
            : clean[..(SubjectWidth - 1)] + "~";
    }
}
